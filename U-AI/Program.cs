using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using UAI.Data;
using UAI.Services;

var builder = WebApplication.CreateBuilder(args);

// Neon hands out URLs with query keys Npgsql rejects (channel_binding) and pooler
// keys that mean the opposite thing to a Npgsql pool (pooling). Normalise both.
static string NormalizeConnectionString(string? raw)
{
    if (string.IsNullOrWhiteSpace(raw)) return "";

    if (Uri.TryCreate(raw.Replace("postgresql://", "postgres://", StringComparison.Ordinal),
                      UriKind.Absolute, out var uri))
    {
        var b = new StringBuilder("Host=").Append(uri.Host)
            .Append(";Port=").Append(uri.IsDefaultPort ? 5432 : uri.Port)
            .Append(";Database=").Append(uri.AbsolutePath.Trim('/'))
            .Append(";Username=").Append(Uri.UnescapeDataString(uri.UserInfo.Split(':')[0]));

        if (uri.UserInfo.Contains(':'))
            b.Append(";Password=").Append(Uri.UnescapeDataString(uri.UserInfo.Split(':', 2)[1]));

        b.Append(";SSL Mode=Require");

        var q = System.Web.HttpUtility.ParseQueryString(uri.Query);
        if (q["sslmode"] is { Length: > 0 } s) b.Append(";SSL Mode=").Append(s);
        if (q["channel_binding"] is { Length: > 0 })
            b.Append(";Trust Server Certificate=true");

        // A pool size of 2 keeps Npgsql's memory tiny; this host has 0.1 CPU anyway.
        b.Append(";Maximum Pool Size=2;Minimum Pool Size=0;Timeout=15;Command Timeout=30");
        return b.ToString();
    }

    return raw;
}

var conn = NormalizeConnectionString(
    Environment.GetEnvironmentVariable("DATABASE_URL")
    ?? builder.Configuration.GetConnectionString("Neon")
    ?? EmbeddedSecrets.GetDatabaseUrl());

if (!string.IsNullOrWhiteSpace(conn))
{
    builder.Services.AddDbContext<AppDbContext>(o => o.UseNpgsql(conn));
}
else
{
    // Keep the app bootable without a database so /healthz can still answer.
    builder.Services.AddDbContext<AppDbContext>(o => o.UseNpgsql(
        "Host=localhost;Database=uai;Username=postgres;Password=postgres"));
}

builder.Services
    .AddIdentity<AppUser, IdentityRole>(o =>
    {
        o.Password.RequiredLength = 6;
        o.Password.RequireNonAlphanumeric = false;
        o.Password.RequireDigit = false;
        o.Password.RequireUppercase = false;
        o.Password.RequireLowercase = false;
        o.User.RequireUniqueEmail = true;
        o.SignIn.RequireConfirmedAccount = false;
        o.Lockout.MaxFailedAccessAttempts = 10;
    })
    .AddEntityFrameworkStores<AppDbContext>()
    .AddDefaultTokenProviders();

// Registered after AddIdentity so it replaces the default PBKDF2 hasher.
builder.Services.AddScoped<IPasswordHasher<AppUser>, FastPasswordHasher<AppUser>>();

builder.Services.ConfigureApplicationCookie(o =>
{
    o.Cookie.Name = "uai.auth";
    o.Cookie.HttpOnly = true;
    o.Cookie.SameSite = SameSiteMode.Lax;
    o.SlidingExpiration = false;         // a rolling cookie keeps the instance "busy"
    o.ExpireTimeSpan = TimeSpan.FromDays(30);
    o.LoginPath = "/account/login";
    o.LogoutPath = "/account/logout";
    o.AccessDeniedPath = "/account/login";
});

builder.Services.AddControllersWithViews();
builder.Services.AddSingleton<OnnxChatService>();

// Per-IP fixed windows on the auth endpoints. This host is a home PC behind
// a tunnel: no WAF, no Cloudflare shield, and a SHA256 password hasher.
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddFixedWindowLimiter("auth-login", x =>
    {
        x.PermitLimit = 5;
        x.Window = TimeSpan.FromMinutes(5);
        x.QueueLimit = 0;
    });
    o.AddFixedWindowLimiter("auth-register", x =>
    {
        x.PermitLimit = 3;
        x.Window = TimeSpan.FromHours(1);
        x.QueueLimit = 0;
    });
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/home/error");
    app.UseHsts();
}

app.UseStaticFiles();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

using (var scope = app.Services.CreateScope())
{
    try
    {
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
        app.Logger.LogInformation("Database migrated");
    }
    catch (Exception ex)
    {
        // Neon scales to zero after 5 min; the first query can be slow or fail.
        app.Logger.LogWarning(ex, "Database unavailable at startup; continuing");
    }
}

// Resolve the singleton from the root provider (safe: singletons outlive
// a scope) so the warm-up task never touches a disposed scope.
var ai = app.Services.GetRequiredService<OnnxChatService>();
_ = Task.Run(() => ai.EnsureLoaded());

// Must answer fast: Render polls this and restarts containers that stall.
app.MapGet("/healthz", (OnnxChatService svc) => Results.Json(new
{
    ok = true,
    model = svc.IsReady ? "ready" : svc.LoadFailed ? "failed" : "loading",
    modelError = svc.LoadError,
    waiting = svc.Waiters,
    build = Environment.GetEnvironmentVariable("RENDER_GIT_COMMIT") is { Length: > 0 } sha
        ? sha[..Math.Min(7, sha.Length)]
        : "local",
}));

app.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}");

app.Run();

public partial class Program;
