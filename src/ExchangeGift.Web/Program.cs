using System.Threading.RateLimiting;
using ExchangeGift.Web.Data;
using ExchangeGift.Web.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// The administrator password comes from User Secrets or environment variables (Admin__Password), never from source files.
// User Secrets are normally Development-only; the start scripts run in Production, so load them explicitly.
if (!builder.Environment.IsEnvironment("Testing"))   // automated tests bring their own settings
{
    builder.Configuration.AddUserSecrets<Program>(optional: true);
    builder.Configuration.AddEnvironmentVariables();
}

// Participant codes are access tokens, so keep them (and SQL) out of routine request logs.
builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Warning);
builder.Logging.AddFilter("Microsoft.AspNetCore.StaticFiles", LogLevel.Warning);
builder.Logging.AddFilter("Microsoft.AspNetCore.Routing", LogLevel.Warning);
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.Warning);

// ---- Storage: the SQLite file and data-protection keys live outside wwwroot and are never served. ----
var storageDir = builder.Configuration["Storage:Directory"];
if (string.IsNullOrWhiteSpace(storageDir))
    storageDir = Path.Combine(builder.Environment.ContentRootPath, "App_Data");
storageDir = Path.GetFullPath(storageDir);
Directory.CreateDirectory(storageDir);

builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseSqlite($"Data Source={Path.Combine(storageDir, "exchangegift.db")}"));

builder.Services.AddDataProtection()
    .SetApplicationName("ExchangeGift")
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(storageDir, "keys")));

// ---- Identity (administrator only) ----
builder.Services.AddIdentity<IdentityUser, IdentityRole>(o =>
    {
        o.Password.RequiredLength = 8;
        o.Password.RequireDigit = true;
        o.Password.RequireLowercase = true;
        o.Password.RequireUppercase = true;
        o.Password.RequireNonAlphanumeric = false;
        o.Lockout.MaxFailedAccessAttempts = 5;
        o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(10);
        o.Lockout.AllowedForNewUsers = true;
        o.User.RequireUniqueEmail = false;
    })
    .AddEntityFrameworkStores<AppDbContext>()
    .AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(o =>
{
    o.Cookie.Name = "ExchangeGift.Admin";
    o.Cookie.HttpOnly = true;
    o.Cookie.SameSite = SameSiteMode.Lax;
    o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest; // https is detected through forwarded headers
    o.LoginPath = "/admin/login";
    o.AccessDeniedPath = "/admin/login";
    o.LogoutPath = "/admin/logout";
    o.ExpireTimeSpan = TimeSpan.FromHours(8);
    o.SlidingExpiration = true;
});

builder.Services.AddAntiforgery(o =>
{
    o.Cookie.Name = "ExchangeGift.Antiforgery";
    o.Cookie.HttpOnly = true;
    o.Cookie.SameSite = SameSiteMode.Lax;
    o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    o.HeaderName = "RequestVerificationToken";
});

builder.Services.AddRazorPages(o =>
{
    o.Conventions.AuthorizeFolder("/Admin");
    o.Conventions.AllowAnonymousToPage("/Admin/Login");
});

var participantLimit = builder.Configuration.GetValue("RateLimit:ParticipantPerMinute", 60);
var loginLimit = builder.Configuration.GetValue("RateLimit:LoginPerMinute", 10);
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    static string Client(HttpContext c) => c.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    // Guessing participant codes or hammering the reveal endpoint.
    o.AddPolicy("participant", ctx => RateLimitPartition.GetFixedWindowLimiter(Client(ctx),
        _ => new FixedWindowRateLimiterOptions { PermitLimit = participantLimit, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    // Password guessing against the administrator login.
    o.AddPolicy("login", ctx => RateLimitPartition.GetFixedWindowLimiter(Client(ctx),
        _ => new FixedWindowRateLimiterOptions { PermitLimit = loginLimit, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});

builder.Services.Configure<AdminOptions>(builder.Configuration.GetSection("Admin"));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<GroupService>();
builder.Services.AddScoped<RevealService>();
builder.Services.AddScoped<SettingsService>();
builder.Services.AddHostedService<DatabaseInitializer>();

builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    // cloudflared connects from loopback, which is trusted by default; this lets the app see the real https scheme.
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.ForwardLimit = 1;
});

var app = builder.Build();

app.UseForwardedHeaders();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
}
else
{
    app.UseDeveloperExceptionPage();
}
app.UseStatusCodePagesWithReExecute("/Error", "?statusCode={0}");

app.Use(async (ctx, next) =>
{
    var h = ctx.Response.Headers;
    h["X-Content-Type-Options"] = "nosniff";
    h["X-Frame-Options"] = "DENY";
    h["Referrer-Policy"] = "no-referrer";
    h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
    h["Content-Security-Policy"] =
        "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; " +
        "font-src 'self'; media-src 'self'; connect-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";
    await next();
});

app.UseStaticFiles();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapRazorPages();

app.Run();

public partial class Program;
