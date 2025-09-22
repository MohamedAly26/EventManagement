using EventManagement.Components;
using EventManagement.Data;
using EventManagement.Models;
using EventManagement.Security;
using EventManagement.Services;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;

var builder = WebApplication.CreateBuilder(args);

// ======================= DataProtection =======================
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(
        Path.Combine(builder.Environment.ContentRootPath, "keys")))
    .SetApplicationName("EventManagement");

builder.Services.Configure<DataProtectionTokenProviderOptions>(o =>
{
    o.TokenLifespan = TimeSpan.FromDays(3);
});

// =========================== Services =========================
builder.Services.AddHttpContextAccessor();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddScoped<EventService>();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents(o => o.DetailedErrors = true);

// HttpClient per Blazor Server (base address = origin del sito)
builder.Services.AddScoped(sp =>
{
    var nav = sp.GetRequiredService<NavigationManager>();
    return new HttpClient { BaseAddress = new Uri(nav.BaseUri) };
});

// ===================== Auth + Identity ========================
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = IdentityConstants.ApplicationScheme;
    options.DefaultChallengeScheme = IdentityConstants.ApplicationScheme;
    options.DefaultSignInScheme = IdentityConstants.ApplicationScheme;
})
.AddIdentityCookies();

builder.Services
    .AddIdentityCore<IdentityUser>(options =>
    {
        options.SignIn.RequireConfirmedAccount = true;
        options.SignIn.RequireConfirmedEmail = true;
        options.User.RequireUniqueEmail = true;

        // password “dev-friendly”
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

// =================== Authorization (permessi) =================
builder.Services.AddAuthorization(options =>
{
    foreach (var perm in Permissions.All)
        options.AddPolicy(perm, p => p.Requirements.Add(new PermissionRequirement(perm)));
});
builder.Services.AddScoped<IAuthorizationHandler, PermissionHandler>();
builder.Services.AddCascadingAuthenticationState();

// (opzionale) segnale UI
builder.Services.AddSingleton<UiSignal>();

// ============================ Email ===========================
builder.Services.Configure<SmtpOptions>(builder.Configuration.GetSection("Smtp"));
builder.Services.AddSingleton<IEmailSender, SmtpEmailSender>();

// ==============================================================
var app = builder.Build();
// ==============================================================

// =========================== Pipeline =========================
app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAntiforgery();
app.UseAuthentication();
app.UseAuthorization();

app.MapRazorComponents<App>()
   .AddInteractiveServerRenderMode();

app.MapIdentityApi<IdentityUser>();

// ==============================================================
/* Account: login / register / confirm / resend / logout */
const string ConfirmPath = "/account/confirm-email";

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
        return Results.Redirect($"/account/login?e=confirm&email={Uri.EscapeDataString(email)}");

    var result = await signIn.PasswordSignInAsync(user, password, isPersistent: true, lockoutOnFailure: false);
    return result.Succeeded ? Results.Redirect("/") : Results.Redirect("/account/login?e=invalid");
}).DisableAntiforgery();

// REGISTER (invia link conferma)
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

// CONFIRM EMAIL
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

// RESEND CONFIRM EMAIL
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

// (opzionale) Diagnostica SMTP
app.MapGet("/diag/smtp", async (IEmailSender mail, HttpContext ctx) =>
{
    var to = ctx.Request.Query["to"].ToString();
    if (string.IsNullOrWhiteSpace(to))
        return Results.BadRequest("Use /diag/smtp?to=address@example.com");

    await mail.SendEmailAsync(to, "SMTP test", "<b>SMTP OK</b>");
    return Results.Ok("Sent.");
});

// ============================ Helpers =========================
static string BuildBaseUrl(IConfiguration config, HttpContext ctx)
{
    var pb = config["PublicBaseUrl"];
    return !string.IsNullOrWhiteSpace(pb)
        ? pb!.TrimEnd('/')
        : $"{ctx.Request.Scheme}://{ctx.Request.Host}";
}

static async Task EnsureRole(RoleManager<IdentityRole> rm, string name)
{
    if (!await rm.RoleExistsAsync(name))
        await rm.CreateAsync(new IdentityRole(name));
}

static async Task Grant(RoleManager<IdentityRole> rm, string roleName, string perm)
{
    var role = await rm.FindByNameAsync(roleName);
    if (role is null) return;
    var claims = await rm.GetClaimsAsync(role);
    if (!claims.Any(c => c.Type == Permissions.ClaimType && c.Value == perm))
        await rm.AddClaimAsync(role, new Claim(Permissions.ClaimType, perm));
}

// =========================== Seed DB ==========================
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;

    var db = services.GetRequiredService<AppDbContext>();
    var roleMgr = services.GetRequiredService<RoleManager<IdentityRole>>();
    var userMgr = services.GetRequiredService<UserManager<IdentityUser>>();

    await db.Database.MigrateAsync();

    // Ruoli
    await EnsureRole(roleMgr, "User");
    await EnsureRole(roleMgr, "Admin");
    await EnsureRole(roleMgr, "Supervisor");

    // Permessi
    await Grant(roleMgr, "Admin", Permissions.Names.ManageEvents);
    await Grant(roleMgr, "Admin", Permissions.Names.ViewSubscribers);

    foreach (var p in Permissions.All)
        await Grant(roleMgr, "Supervisor", p);

    // Admin seed
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
    }
    if (!await userMgr.IsInRoleAsync(admin, "Admin"))
        await userMgr.AddToRoleAsync(admin, "Admin");
    if (await userMgr.IsInRoleAsync(admin, "Supervisor"))
        await userMgr.RemoveFromRoleAsync(admin, "Supervisor");

    // Supervisor seed
    const string SupervisorEmail = "mohamedessamrere@gmail.com";
    var sup = await userMgr.FindByEmailAsync(SupervisorEmail);
    if (sup is null)
    {
        sup = new IdentityUser
        {
            UserName = SupervisorEmail,
            Email = SupervisorEmail,
            EmailConfirmed = true
        };
        await userMgr.CreateAsync(sup, "Supervisor123!");
    }
    if (!await userMgr.IsInRoleAsync(sup, "Supervisor"))
        await userMgr.AddToRoleAsync(sup, "Supervisor");
    if (!await userMgr.IsInRoleAsync(sup, "Admin"))
        await userMgr.AddToRoleAsync(sup, "Admin");

    // Eventi demo
    if (!db.Events.Any())
    {
        db.Events.AddRange(
            new Event { Title = "Music Festival", StartDateTime = DateTime.Now.AddDays(5), Location = "Rome", MaxParticipants = 200 },
            new Event { Title = "Tech Conference", StartDateTime = DateTime.Now.AddDays(10), Location = "Milan", MaxParticipants = 500 },
            new Event { Title = "Art Exhibition", StartDateTime = DateTime.Now.AddDays(2), Location = "Florence", MaxParticipants = 100 }
        );
        await db.SaveChangesAsync();
    }
}

app.Run();
