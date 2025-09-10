using EventManagement.Components;
using EventManagement.Data;
using EventManagement.Models;
using EventManagement.Services;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

using System.Text;
using System.Text.Encodings.Web;
using System.Security.Claims;
using EventManagement.Security;

var builder = WebApplication.CreateBuilder(args);

// ---------------- DataProtection / token lifetime ----------------
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(
        Path.Combine(builder.Environment.ContentRootPath, "keys")))
    .SetApplicationName("EventManagement");

builder.Services.Configure<DataProtectionTokenProviderOptions>(o =>
{
    o.TokenLifespan = TimeSpan.FromDays(3);
});

// ---------------- Core services ----------------
builder.Services.AddHttpContextAccessor();
builder.Services.AddHttpClient();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddScoped<EventService>();
// HttpClient per Blazor Server: imposta la BaseAddress all'origin dell'app
// HttpClient per Blazor Server: usa l'origin corrente come BaseAddress
builder.Services.AddScoped(sp =>
{
    var nav = sp.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
    return new HttpClient { BaseAddress = new Uri(nav.BaseUri) };
});


builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents(o => o.DetailedErrors = true);

// ---------------- Auth / Identity ----------------
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = IdentityConstants.ApplicationScheme;
    options.DefaultChallengeScheme = IdentityConstants.ApplicationScheme;
    options.DefaultSignInScheme = IdentityConstants.ApplicationScheme;
})
.AddIdentityCookies();

// permission policies (claim-based)
builder.Services.AddAuthorization(options =>
{
    foreach (var perm in Permissions.All)
        options.AddPolicy(perm, p => p.RequireClaim(Permissions.ClaimType, perm));
});

builder.Services.AddCascadingAuthenticationState();

// SMTP + IEmailSender
builder.Services.Configure<SmtpOptions>(builder.Configuration.GetSection("Smtp"));
builder.Services.AddSingleton<IEmailSender, SmtpEmailSender>();

// Identity (con token conferma email)
builder.Services
    .AddIdentityCore<IdentityUser>(options =>
    {
        options.SignIn.RequireConfirmedAccount = true;
        options.SignIn.RequireConfirmedEmail = true;
        options.User.RequireUniqueEmail = true;

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

var app = builder.Build();

// ---------------- Pipeline ----------------
app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAntiforgery();
app.UseAuthentication();
app.UseAuthorization();

app.MapRazorComponents<App>()
   .AddInteractiveServerRenderMode();

// Minimal Identity endpoints
app.MapIdentityApi<IdentityUser>();

// =================================================================
// Admin API: utenti e permessi
// =================================================================

// GET /admin/users  -> elenco utenti con ruoli
app.MapGet("/admin/users",
    [Authorize(Policy = Permissions.Names.ManageUsers)]
async (UserManager<IdentityUser> userMgr) =>
    {
        var users = await userMgr.Users.ToListAsync();
        var data = new List<object>(users.Count);
        foreach (var u in users)
        {
            var roles = await userMgr.GetRolesAsync(u);
            data.Add(new { u.Id, u.Email, u.EmailConfirmed, Roles = roles });
        }
        return Results.Json(data);
    });

// GET /admin/permissions  -> mappa ruolo -> permessi
app.MapGet("/admin/permissions",
    [Authorize(Policy = Permissions.Names.ManageRoles)]
async (RoleManager<IdentityRole> roleMgr) =>
    {
        var roles = roleMgr.Roles.ToList();
        var result = new Dictionary<string, string[]>();
        foreach (var r in roles)
        {
            var claims = await roleMgr.GetClaimsAsync(r);
            result[r.Name!] = claims
                .Where(c => c.Type == Permissions.ClaimType)
                .Select(c => c.Value)
                .OrderBy(x => x)
                .ToArray();
        }
        return Results.Json(result);
    });

// POST /admin/permissions/grant?role=Admin&perm=events.manage
app.MapPost("/admin/permissions/grant",
    [Authorize(Policy = Permissions.Names.ManageRoles)]
async (string role, string perm, RoleManager<IdentityRole> roleMgr) =>
    {
        if (!Permissions.All.Contains(perm))
            return Results.BadRequest("Unknown permission.");
        var r = await roleMgr.FindByNameAsync(role);
        if (r is null) return Results.NotFound("Role not found.");
        var claims = await roleMgr.GetClaimsAsync(r);
        if (!claims.Any(c => c.Type == Permissions.ClaimType && c.Value == perm))
            await roleMgr.AddClaimAsync(r, new Claim(Permissions.ClaimType, perm));
        return Results.Ok();
    })
.DisableAntiforgery();

// POST /admin/permissions/revoke?role=Admin&perm=events.manage
app.MapPost("/admin/permissions/revoke",
    [Authorize(Policy = Permissions.Names.ManageRoles)]
async (string role, string perm, RoleManager<IdentityRole> roleMgr) =>
    {
        var r = await roleMgr.FindByNameAsync(role);
        if (r is null) return Results.NotFound("Role not found.");
        var claims = await roleMgr.GetClaimsAsync(r);
        var hit = claims.FirstOrDefault(c => c.Type == Permissions.ClaimType && c.Value == perm);
        if (hit is null) return Results.NotFound("Claim not found.");
        await roleMgr.RemoveClaimAsync(r, hit);
        return Results.Ok();
    })
.DisableAntiforgery();

// ---------------- Role Management (Supervisor only) ----------------

// Aggiungi un ruolo a un utente
// POST /admin/roles/add?email=user@x.com&role=Admin
app.MapPost("/admin/roles/add",
    [Authorize(Policy = Permissions.Names.ManageRoles)]
async (string email, string role, UserManager<IdentityUser> userMgr, RoleManager<IdentityRole> roleMgr) =>
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(role))
            return Results.BadRequest("email and role are required.");

        if (!await roleMgr.RoleExistsAsync(role))
            await roleMgr.CreateAsync(new IdentityRole(role));

        var user = await userMgr.FindByEmailAsync(email);
        if (user is null) return Results.NotFound("User not found.");

        if (await userMgr.IsInRoleAsync(user, role))
            return Results.Ok("User already in that role.");

        var res = await userMgr.AddToRoleAsync(user, role);
        return res.Succeeded ? Results.Ok("Role added.") :
            Results.BadRequest(string.Join("; ", res.Errors.Select(e => e.Description)));
    })
.DisableAntiforgery();

// Rimuovi un ruolo da un utente (con safeguard per l’ultimo Admin)
// POST /admin/roles/remove?email=user@x.com&role=Admin
app.MapPost("/admin/roles/remove",
    [Authorize(Policy = Permissions.Names.ManageRoles)]
async (string email, string role, UserManager<IdentityUser> userMgr) =>
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(role))
            return Results.BadRequest("email and role are required.");

        var user = await userMgr.FindByEmailAsync(email);
        if (user is null) return Results.NotFound("User not found.");

        if (!await userMgr.IsInRoleAsync(user, role))
            return Results.Ok("User not in that role.");

        // safeguard: non togliere Admin all’ultimo admin rimasto
        if (role.Equals("Admin", StringComparison.OrdinalIgnoreCase))
        {
            var admins = await userMgr.GetUsersInRoleAsync("Admin");
            if (admins.Count <= 1 && admins.Any(u => u.Id == user.Id))
                return Results.BadRequest("Cannot remove 'Admin' from the last remaining admin.");
        }

        var res = await userMgr.RemoveFromRoleAsync(user, role);
        return res.Succeeded ? Results.Ok("Role removed.") :
            Results.BadRequest(string.Join("; ", res.Errors.Select(e => e.Description)));
    })
.DisableAntiforgery();

// =================================================================
// Email flows (login/register/confirm/resend)
// =================================================================

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

// RESEND CONFIRMATION
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

// ---------------- Helpers ----------------
static string BuildBaseUrl(IConfiguration config, HttpContext ctx)
{
    var pb = config["PublicBaseUrl"];
    return !string.IsNullOrWhiteSpace(pb) ? pb!.TrimEnd('/') : $"{ctx.Request.Scheme}://{ctx.Request.Host}";
}

static async Task EnsureRole(RoleManager<IdentityRole> rm, string name)
{
    if (!await rm.RoleExistsAsync(name))
        await rm.CreateAsync(new IdentityRole(name));
}

static async Task Grant(RoleManager<IdentityRole> rm, string roleName, string perm)
{
    var role = await rm.FindByNameAsync(roleName);
    var claims = await rm.GetClaimsAsync(role);
    if (!claims.Any(c => c.Type == Permissions.ClaimType && c.Value == perm))
        await rm.AddClaimAsync(role, new Claim(Permissions.ClaimType, perm));
}

// =================================================================
// SEED: ruoli, permessi, admin, SUPERVisor, eventi demo
// =================================================================
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;

    var db = services.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();

    var roleMgr = services.GetRequiredService<RoleManager<IdentityRole>>();
    await EnsureRole(roleMgr, "User");
    await EnsureRole(roleMgr, "Admin");
    await EnsureRole(roleMgr, "Supervisor");

    // Admin: NO ManageRoles (così solo il Supervisor gestisce i ruoli)
    await Grant(roleMgr, "Admin", Permissions.Names.ManageEvents);
    await Grant(roleMgr, "Admin", Permissions.Names.ViewSubscribers);
    await Grant(roleMgr, "Admin", Permissions.Names.ManageUsers);

    // Supervisor ha TUTTI i permessi
    foreach (var p in Permissions.All)
        await Grant(roleMgr, "Supervisor", p);

    var userMgr = services.GetRequiredService<UserManager<IdentityUser>>();

    // Admin seed (rimane Admin)
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

    // >>> Supervisor: promuovi l'email richiesta
    var supervisorEmail = "mohamedessamrere@gmail.com";
    var sup = await userMgr.FindByEmailAsync(supervisorEmail);
    if (sup is null)
    {
        // lo creo in dev; confermato per facilitare l’accesso
        sup = new IdentityUser
        {
            UserName = supervisorEmail,
            Email = supervisorEmail,
            EmailConfirmed = true
        };
        await userMgr.CreateAsync(sup, "Supervisor123!"); // password dev
    }
    if (!await userMgr.IsInRoleAsync(sup, "Supervisor"))
        await userMgr.AddToRoleAsync(sup, "Supervisor");

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
