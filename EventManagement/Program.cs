using EventManagement.Components;
using EventManagement.Data;
using EventManagement.Models;
using EventManagement.Services;
using Microsoft.AspNetCore.Builder.Extensions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.WebUtilities;
using System.Text;
using System.Text.Encodings.Web;

var builder = WebApplication.CreateBuilder(args);

// --- SERVICES (tutti prima di Build) ---
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<EventService>();
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents(o => o.DetailedErrors = true); // <— QUI va bene

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = IdentityConstants.ApplicationScheme;
    options.DefaultChallengeScheme = IdentityConstants.ApplicationScheme;
    options.DefaultSignInScheme = IdentityConstants.ApplicationScheme;
})
.AddIdentityCookies();

builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();


// SMTP settings from configuration
builder.Services.Configure<SmtpOptions>(builder.Configuration.GetSection("Smtp"));

// Register our SMTP sender for Identity's IEmailSender
builder.Services.AddSingleton<IEmailSender, SmtpEmailSender>();

// Identity with tokens (confirmation)
builder.Services
    .AddIdentityCore<IdentityUser>(options =>
    {
        options.SignIn.RequireConfirmedEmail = true;
        options.User.RequireUniqueEmail = true;

        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequireUppercase = false;
        options.Password.RequireLowercase = false;
        options.Password.RequireDigit = false;
        options.Password.RequiredLength = 6;
        options.SignIn.RequireConfirmedAccount = true;
        options.SignIn.RequireConfirmedEmail = true;
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<AppDbContext>()
    .AddSignInManager()
    .AddApiEndpoints()
    .AddDefaultTokenProviders(); // <-- important

// Email sender (alias dell'interfaccia UI)
builder.Services.AddTransient<
    Microsoft.AspNetCore.Identity.UI.Services.IEmailSender,
    EventManagement.Services.SmtpEmailSender>();

// --- BUILD ---
var app = builder.Build();

// --- PIPELINE / ENDPOINTS ---
app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAntiforgery();
app.UseAuthentication();
app.UseAuthorization();

app.MapRazorComponents<App>()
   .AddInteractiveServerRenderMode();

app.MapIdentityApi<IdentityUser>();

app.MapPost("/app-login", async (HttpContext ctx, SignInManager<IdentityUser> signIn) =>
{
    var form = await ctx.Request.ReadFormAsync();
    var email = form["email"].ToString();
    var password = form["password"].ToString();

    var result = await signIn.PasswordSignInAsync(email, password, isPersistent: true, lockoutOnFailure: false);
    if (result.Succeeded)
        return Results.Redirect("/"); // home dopo login

    return Results.Redirect("/account/login?e=1"); // errore
}).DisableAntiforgery(); // semplifichiamo: niente antiforgery su questo endpoint

// REGISTER via form + ruolo "User" + login automatico
app.MapPost("/app-register", async (
    HttpContext ctx,
    UserManager<IdentityUser> users,
    RoleManager<IdentityRole> roles,
    SignInManager<IdentityUser> signIn) =>
{
    var form = await ctx.Request.ReadFormAsync();
    var email = form["email"].ToString();
    var password = form["password"].ToString();

    var user = new IdentityUser { UserName = email, Email = email, EmailConfirmed = true };
    var create = await users.CreateAsync(user, password);
    if (!create.Succeeded)
    {
        var msg = string.Join("; ", create.Errors.Select(e => e.Description));
        return Results.Redirect($"/account/register?e={Uri.EscapeDataString(msg)}");
    }

    if (!await roles.RoleExistsAsync("User"))
        await roles.CreateAsync(new IdentityRole("User"));
    await users.AddToRoleAsync(user, "User");

    await signIn.SignInAsync(user, isPersistent: true);
    return Results.Redirect("/");
}).DisableAntiforgery();

// ---- SEED: migrazione schema + ruoli + admin ----
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
app.MapPost("/app-logout", async (SignInManager<IdentityUser> signIn) =>
{
    await signIn.SignOutAsync();     // invalida il cookie
    return Results.Redirect("/");    // torna alla home
}).DisableAntiforgery();
app.Run();
