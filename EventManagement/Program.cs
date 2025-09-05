using EventManagement.Components;
using EventManagement.Data;
using EventManagement.Models;
using EventManagement.Services;

using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.WebUtilities;
using System.Text;
using System.Text.Encodings.Web;

var builder = WebApplication.CreateBuilder(args);

// ---------------------- SERVICES ----------------------
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

// SMTP configuration + email sender
builder.Services.Configure<SmtpOptions>(builder.Configuration.GetSection("Smtp"));
builder.Services.AddSingleton<IEmailSender, SmtpEmailSender>(); // single IEmailSender registration

// Identity (with tokens for email confirmation)
builder.Services
    .AddIdentityCore<IdentityUser>(options =>
    {
        // Require confirmation
        options.SignIn.RequireConfirmedAccount = true;
        options.SignIn.RequireConfirmedEmail = true;

        options.User.RequireUniqueEmail = true;

        // Dev-friendly password rules
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
    .AddDefaultTokenProviders(); // <-- needed for confirmation tokens

// ---------------------- BUILD ----------------------
var app = builder.Build();

// ---------------------- PIPELINE ----------------------

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAntiforgery();
app.UseAuthentication();
app.UseAuthorization();

app.MapRazorComponents<App>()
   .AddInteractiveServerRenderMode();

// Minimal Identity endpoints (/login, /logout, /register, etc. – plus our own)
app.MapIdentityApi<IdentityUser>();


// ---------------------- CUSTOM ENDPOINTS ----------------------

// LOGIN (form post) – resolves user by email and checks confirmation
app.MapPost("/app-login", async (
    HttpContext ctx,
    SignInManager<IdentityUser> signIn,
    UserManager<IdentityUser> users,
    ILoggerFactory loggerFactory) =>
{
    var logger = loggerFactory.CreateLogger("Login");

    try
    {
        var form = await ctx.Request.ReadFormAsync();
        var email = form["email"].ToString();
        var password = form["password"].ToString();

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            return Results.Redirect("/account/login?e=invalid");

        // Find by email, fallback to username
        var user = await users.FindByEmailAsync(email)
                   ?? await users.FindByNameAsync(email);
        if (user is null)
        {
            logger.LogWarning("Login failed: user not found for {Email}", email);
            return Results.Redirect("/account/login?e=invalid");
        }

        if (users.Options.SignIn.RequireConfirmedAccount && !user.EmailConfirmed)
        {
            logger.LogInformation("Login blocked (email not confirmed) for {Email}", email);
            return Results.Redirect("/account/login?e=confirm");
        }

        var result = await signIn.PasswordSignInAsync(user.UserName!, password,
                                                      isPersistent: true, lockoutOnFailure: false);

        if (result.Succeeded)
            return Results.Redirect("/");

        logger.LogWarning("Login failed: bad credentials for {Email}", email);
        return Results.Redirect("/account/login?e=invalid");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Unhandled error during login");
        return Results.Redirect("/account/login?e=err");
    }
}).DisableAntiforgery();


// REGISTER (form post) – creates user, assigns "User", sends confirmation email
app.MapPost("/app-register", async (
    HttpContext ctx,
    UserManager<IdentityUser> users,
    RoleManager<IdentityRole> roles,
    IEmailSender emailSender,
    ILoggerFactory loggerFactory) =>
{
    var log = loggerFactory.CreateLogger("Register");

    try
    {
        var form = await ctx.Request.ReadFormAsync();
        var name = form["name"].ToString();
        var email = form["email"].ToString();
        var password = form["password"].ToString();
        var password2 = form["password2"].ToString(); // conferma

        // 1) Validazioni di base
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            return Results.Redirect("/account/register?e=Missing+email+or+password");

        if (password != password2)
            return Results.Redirect("/account/register?e=Passwords+do+not+match");

        // 2) Utente già esistente?
        var existing = await users.FindByEmailAsync(email) ?? await users.FindByNameAsync(email);
        if (existing is not null)
        {
            // Se non confermato → chiedi di confermare (o prevedi “Resend confirmation”)
            if (!existing.EmailConfirmed)
                return Results.Redirect("/account/login?confirm=1");
            return Results.Redirect("/account/register?e=Email+already+registered");
        }

        // 3) Crea utente NON confermato
        var user = new IdentityUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = false
        };

        var create = await users.CreateAsync(user, password);
        if (!create.Succeeded)
        {
            var msg = string.Join("; ", create.Errors.Select(e => e.Description));
            return Results.Redirect($"/account/register?e={Uri.EscapeDataString(msg)}");
        }

        // 4) Assicura ruolo “User”
        if (!await roles.RoleExistsAsync("User"))
            await roles.CreateAsync(new IdentityRole("User"));
        await users.AddToRoleAsync(user, "User");

        // 5) Token di conferma + link
        var token = await users.GenerateEmailConfirmationTokenAsync(user);
        var code = Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));

        var req = ctx.Request;
        var baseUrl = $"{req.Scheme}://{req.Host}";
        var confirmUrl = $"{baseUrl}/account/confirm?userId={Uri.EscapeDataString(user.Id)}&code={code}";

        // 6) Invia email (protetta da try/catch)
        var subject = "Confirm your email";
        var body = $"""
                    Hi{(string.IsNullOrWhiteSpace(name) ? "" : " " + System.Text.Encodings.Web.HtmlEncoder.Default.Encode(name))},<br/>
                    Please confirm your account by <a href='{System.Text.Encodings.Web.HtmlEncoder.Default.Encode(confirmUrl)}'>clicking here</a>.
                    """;

        try
        {
            await emailSender.SendEmailAsync(email, subject, body);
        }
        catch (Exception ex)
        {
            // In dev va benissimo: metti il link nei log
            log.LogWarning(ex, "Email send failed. Confirmation URL: {Url}", confirmUrl);
        }

        // 7) Messaggio: controlla la casella e conferma
        return Results.Redirect("/account/login?confirm=1");
    }
    catch (Exception ex)
    {
        var msg = ex.Message.Replace('"', '\''); // no quote injection
        log.LogError(ex, "Unhandled error in /app-register");
        return Results.Redirect($"/account/register?e={Uri.EscapeDataString("Unexpected error")}");
    }
}).DisableAntiforgery();

// Endpoint di conferma <<<
app.MapGet("/account/confirm", async (
    string userId,
    string code,
    UserManager<IdentityUser> users) =>
{
    if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(code))
        return Results.BadRequest("Missing parameters.");

    var user = await users.FindByIdAsync(userId);
    if (user is null)
        return Results.BadRequest("Invalid user.");

    // decode del token
    var decoded = WebEncoders.Base64UrlDecode(code);
    var token = Encoding.UTF8.GetString(decoded);

    var result = await users.ConfirmEmailAsync(user, token);
    if (result.Succeeded)
    {
        // banner “confirmed” sulla pagina di login
        return Results.Redirect("/account/login?confirmed=1");
    }

    var err = string.Join("; ", result.Errors.Select(e => e.Description));
    return Results.Content(
        $"<h3>Email confirmation failed</h3><p>{err}</p>",
        "text/html");
});

// LOGOUT
app.MapPost("/app-logout", async (SignInManager<IdentityUser> signIn) =>
{
    await signIn.SignOutAsync();
    return Results.Redirect("/");
}).DisableAntiforgery();

// ---------------------- SEED (DB + roles + admin) ----------------------
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
            EmailConfirmed = true // seed admin already confirmed so login works
        };
        await userMgr.CreateAsync(admin, "Admin123!");
        await userMgr.AddToRoleAsync(admin, "Admin");
    }
    app.MapGet("/diag/identity", async (UserManager<IdentityUser> users) =>
    {
        var admin = await users.FindByEmailAsync("admin@example.com");
        return Results.Json(new
        {
            HasAdmin = admin is not null,
            AdminConfirmed = admin?.EmailConfirmed ?? false
        });
    });


}

app.Run();
