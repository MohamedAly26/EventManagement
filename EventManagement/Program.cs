using EventManagement.Components;
using EventManagement.Data;
using EventManagement.Models;
using EventManagement.Services;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using System.Text;
using System.Text.Encodings.Web;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------
// DataProtection so Identity tokens survive restarts (store keys on disk)
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(
        Path.Combine(builder.Environment.ContentRootPath, "keys")))
    .SetApplicationName("EventManagement");

// Optionally extend token lifetime (default is ~24h)
builder.Services.Configure<DataProtectionTokenProviderOptions>(o =>
{
    o.TokenLifespan = TimeSpan.FromDays(3);
});

// ---------------------------------------------------------------------
// Services
builder.Services.AddHttpContextAccessor();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddScoped<EventService>();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents(o => o.DetailedErrors = true);

// Auth + Identity
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = IdentityConstants.ApplicationScheme;
    options.DefaultChallengeScheme = IdentityConstants.ApplicationScheme;
    options.DefaultSignInScheme = IdentityConstants.ApplicationScheme;
})
.AddIdentityCookies();

builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();

// SMTP + email sender
builder.Services.Configure<SmtpOptions>(builder.Configuration.GetSection("Smtp"));
builder.Services.AddSingleton<IEmailSender, SmtpEmailSender>();

// Identity with tokens for email confirmation
builder.Services
    .AddIdentityCore<IdentityUser>(options =>
    {
        options.SignIn.RequireConfirmedAccount = true;
        options.SignIn.RequireConfirmedEmail = true;
        options.User.RequireUniqueEmail = true;

        // dev-friendly password rules
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequireUppercase = false;
        options.Password.RequireLowercase = false;
        options.Password.RequireDigit = false;
        options.Password.RequiredLength = 6;
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<AppDbContext>()
    .AddSignInManager()
    .AddApiEndpoints()
    .AddDefaultTokenProviders();

// ---------------------------------------------------------------------
var app = builder.Build();

// ---------------------------------------------------------------------
// Pipeline
app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAntiforgery();
app.UseAuthentication();
app.UseAuthorization();

app.MapRazorComponents<App>()
   .AddInteractiveServerRenderMode();

// Minimal Identity endpoints (/login, /logout, /register, etc.)
app.MapIdentityApi<IdentityUser>();

// ---------------------------------------------------------------------
// Custom endpoints
const string ConfirmPath = "/account/confirm-email"; // <- single source of truth

// LOGIN
app.MapPost("/app-login", async (
    HttpContext ctx,
    SignInManager<IdentityUser> signIn,
    UserManager<IdentityUser> users) =>
{
    var form = await ctx.Request.ReadFormAsync();
    var email = form["email"].ToString();
    var password = form["password"].ToString();

    var user = await users.FindByEmailAsync(email) ?? await users.FindByNameAsync(email);
    if (user is null) return Results.Redirect("/account/login?e=invalid");

    if (users.Options.SignIn.RequireConfirmedAccount && !user.EmailConfirmed)
        return Results.Redirect("/account/login?e=confirm");

    var result = await signIn.PasswordSignInAsync(user, password, isPersistent: true, lockoutOnFailure: false);
    return result.Succeeded ? Results.Redirect("/") : Results.Redirect("/account/login?e=invalid");
}).DisableAntiforgery();

// REGISTER (send confirmation link)
app.MapPost("/app-register", async (
    HttpContext ctx,
    UserManager<IdentityUser> users,
    RoleManager<IdentityRole> roles,
    IEmailSender emailSender,
    IConfiguration config,
    ILoggerFactory lf) =>
{
    var log = lf.CreateLogger("Register");

    var form = await ctx.Request.ReadFormAsync();
    var email = form["email"].ToString();
    var password = form["password"].ToString();
    var name = form["name"].ToString();

    var user = new IdentityUser { UserName = email, Email = email, EmailConfirmed = false };
    var create = await users.CreateAsync(user, password);
    if (!create.Succeeded)
    {
        var msg = string.Join("; ", create.Errors.Select(e => e.Description));
        return Results.Redirect($"/account/register?e={Uri.EscapeDataString(msg)}");
    }

    if (!await roles.RoleExistsAsync("User"))
        await roles.CreateAsync(new IdentityRole("User"));
    await users.AddToRoleAsync(user, "User");

    var token = await users.GenerateEmailConfirmationTokenAsync(user);
    var code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));

    var baseUrl = BuildBaseUrl(config, ctx);
    var confirmUrl = $"{baseUrl}{ConfirmPath}?userId={Uri.EscapeDataString(user.Id)}&code={code}";

    var subject = "Confirm your email";
    var body = $"""
        Hi{(string.IsNullOrWhiteSpace(name) ? "" : " " + HtmlEncoder.Default.Encode(name))},
        <br/>Please confirm your account by
        <a href="{HtmlEncoder.Default.Encode(confirmUrl)}">clicking here</a>.
    """;

    try { await emailSender.SendEmailAsync(email, subject, body); }
    catch (Exception ex) { log.LogWarning(ex, "Email send failed. Confirmation URL: {Url}", confirmUrl); }

    return Results.Redirect("/account/login?confirm=1");
}).DisableAntiforgery();

// CONFIRM EMAIL (GET /account/confirm-email)
app.MapGet(ConfirmPath, async (
    string userId,
    string code,
    UserManager<IdentityUser> users) =>
{
    if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(code))
        return Results.BadRequest("Missing data.");

    var user = await users.FindByIdAsync(userId);
    if (user is null) return Results.BadRequest("Invalid user.");

    var token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(code));
    var res = await users.ConfirmEmailAsync(user, token);

    if (res.Succeeded)
        return Results.Redirect("/account/login?confirmed=1");

    var msg = string.Join("; ", res.Errors.Select(e => e.Description));
    return Results.Redirect($"/account/login?e={Uri.EscapeDataString(msg)}");
});

// RESEND CONFIRMATION (uses the SAME ConfirmPath)
app.MapPost("/account/resend-confirmation", async (
    HttpContext ctx,
    UserManager<IdentityUser> users,
    IEmailSender emailSender,
    IConfiguration config) =>
{
    var form = await ctx.Request.ReadFormAsync();
    var email = form["email"].ToString();

    var user = await users.FindByEmailAsync(email);
    if (user is null || user.EmailConfirmed)
        return Results.Redirect("/account/login?e=confirm");

    var token = await users.GenerateEmailConfirmationTokenAsync(user);
    var code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));

    var baseUrl = BuildBaseUrl(config, ctx);
    var confirmUrl = $"{baseUrl}{ConfirmPath}?userId={Uri.EscapeDataString(user.Id)}&code={code}";

    await emailSender.SendEmailAsync(email, "Confirm your email",
        $"Click <a href='{HtmlEncoder.Default.Encode(confirmUrl)}'>here</a> to confirm.");

    return Results.Redirect("/account/login?sent=1");
}).DisableAntiforgery();

// LOGOUT
app.MapPost("/app-logout", async (SignInManager<IdentityUser> signIn) =>
{
    await signIn.SignOutAsync();
    return Results.Redirect("/");
}).DisableAntiforgery();

// Optional SMTP diag
app.MapGet("/diag/smtp", async (IEmailSender mail, HttpContext ctx) =>
{
    var to = ctx.Request.Query["to"].ToString();
    if (string.IsNullOrWhiteSpace(to))
        return Results.BadRequest("Use /diag/smtp?to=address@example.com");

    await mail.SendEmailAsync(to, "SMTP test", "<b>SMTP OK</b>");
    return Results.Ok("Sent.");
});

// Helper to build a public base URL (use appsettings: "PublicBaseUrl" for LAN/WAN)
static string BuildBaseUrl(IConfiguration config, HttpContext ctx)
{
    var pb = config["PublicBaseUrl"];
    return !string.IsNullOrWhiteSpace(pb)
        ? pb!.TrimEnd('/')
        : $"{ctx.Request.Scheme}://{ctx.Request.Host}";
}

// ---------------------------------------------------------------------
// Seed DB (roles, admin, sample events)
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;

    var db = services.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();

    if (!db.Events.Any())
    {
        db.Events.AddRange(
            new Event { Title = "Music Festival", StartDateTime = DateTime.Now.AddDays(5), Location = "Rome", MaxParticipants = 200 },
            new Event { Title = "Tech Conference", StartDateTime = DateTime.Now.AddDays(10), Location = "Milan", MaxParticipants = 500 },
            new Event { Title = "Art Exhibition", StartDateTime = DateTime.Now.AddDays(2), Location = "Florence", MaxParticipants = 100 }
        );
        await db.SaveChangesAsync();
    }

    var roleMgr = services.GetRequiredService<RoleManager<IdentityRole>>();
    if (!await roleMgr.RoleExistsAsync("Admin")) await roleMgr.CreateAsync(new IdentityRole("Admin"));
    if (!await roleMgr.RoleExistsAsync("User")) await roleMgr.CreateAsync(new IdentityRole("User"));

    var userMgr = services.GetRequiredService<UserManager<IdentityUser>>();
    var admin = await userMgr.FindByEmailAsync("admin@example.com");
    if (admin is null)
    {
        admin = new IdentityUser
        {
            UserName = "admin@example.com",
            Email = "admin@example.com",
            EmailConfirmed = true
        };
        await userMgr.CreateAsync(admin, "Admin123!");
        await userMgr.AddToRoleAsync(admin, "Admin");
    }

}

app.Run();
