using GeniusOnline.Data;
using GeniusOnline.Devices;
using GeniusOnline.Web.Auth;
using GeniusOnline.Web.Components;
using GeniusOnline.Web.Hubs;
using GeniusOnline.Web.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using MudBlazor.Services;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

if (!builder.WebHost.GetSetting("urls")?.Contains(':') ?? true)
{
    builder.WebHost.UseUrls("http://0.0.0.0:9050");
}

builder.Host.UseSerilog((ctx, lc) => lc
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console());

builder.Services.AddGeniusDevices(builder.Configuration);
builder.Services.AddGeniusData(builder.Configuration);
builder.Services.AddSingleton<DeviceLiveStore>();
builder.Services.AddSingleton<RuntimeConfigStore>();
builder.Services.AddHostedService<DeviceHubBroadcaster>();
builder.Services.AddSignalR();
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddMudServices();
builder.Services.AddCascadingAuthenticationState();

builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection("Auth"));
builder.Services.AddSingleton<LoginRateLimiter>();
builder.Services.AddScoped<AuthService>();

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.LoginPath = "/login";
        o.LogoutPath = "/logout";
        o.Cookie.Name = "GeniusOnline.Auth";
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Lax;
        o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        o.SlidingExpiration = true;
        o.ExpireTimeSpan = TimeSpan.FromHours(8);
    });

// Authorization is enforced per-page via [Authorize] on each Razor component
// (Dashboard, Rotator, Antenna, Power, Tuner). Static files, /login and /health
// stay anonymous so the favicon redirect loop that breaks antiforgery can't happen.
builder.Services.AddAuthorization();
builder.Services.AddAntiforgery();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var dbf = scope.ServiceProvider.GetRequiredService<Microsoft.EntityFrameworkCore.IDbContextFactory<GeniusDbContext>>();
    using var db = dbf.CreateDbContext();
    db.Database.EnsureCreated();
}

// Apply persisted device config (DB), seeding from appsettings on first run.
_ = Task.Run(async () =>
{
    var store = app.Services.GetRequiredService<RuntimeConfigStore>();
    var host = app.Services.GetRequiredService<DeviceRegistryHost>();
    var devices = await store.GetDevicesAsync();
    await host.ApplyConfigAsync(devices);

    store.DevicesChanged += (_, _) =>
    {
        _ = Task.Run(async () =>
        {
            var fresh = await store.GetDevicesAsync();
            await host.ApplyConfigAsync(fresh);
        });
    };
});

app.UseSerilogRequestLogging();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapHub<DeviceHub>("/hubs/devices");

app.MapGet("/health", () => Results.Ok(new
{
    status = "ok",
    timestamp = DateTimeOffset.UtcNow,
})).AllowAnonymous();

// Login POST: handled outside the antiforgery pipeline. There is no pre-existing
// session to forge against, and SameSite=Lax already blocks cross-site form posts.
app.MapPost("/login/submit", async (
        HttpContext ctx,
        AuthService auth,
        [FromForm] string password,
        [FromForm] bool? rememberMe,
        [FromForm] string? returnUrl) =>
    {
        var result = await auth.LoginAsync(ctx, password ?? string.Empty, rememberMe ?? false);
        if (!result.Success)
        {
            var err = Uri.EscapeDataString(result.Error ?? "Invalid credentials");
            var back = Uri.EscapeDataString(returnUrl ?? "/");
            return Results.Redirect($"/login?error={err}&returnUrl={back}");
        }

        var target = string.IsNullOrEmpty(returnUrl) ? "/" : returnUrl;
        return Results.Redirect(target);
    })
    .AllowAnonymous()
    .DisableAntiforgery();

app.MapPost("/logout", async (HttpContext ctx, AuthService auth) =>
{
    await auth.LogoutAsync(ctx);
    return Results.Redirect("/login");
}).DisableAntiforgery();

app.MapGet("/logout", async (HttpContext ctx, AuthService auth) =>
{
    await auth.LogoutAsync(ctx);
    return Results.Redirect("/login");
});

app.Run();
