using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Options;

namespace GeniusOnline.Web.Auth;

public sealed class AuthService
{
    private readonly IOptionsMonitor<AuthOptions> _options;
    private readonly LoginRateLimiter _limiter;

    public AuthService(IOptionsMonitor<AuthOptions> options, LoginRateLimiter limiter)
    {
        _options = options;
        _limiter = limiter;
    }

    public AuthMode Mode => _options.CurrentValue.Mode;

    public async Task<LoginResult> LoginAsync(HttpContext ctx, string password, bool rememberMe)
    {
        var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        if (!_limiter.TryRegister(ip))
        {
            return new LoginResult(false, "Too many attempts, please wait a minute");
        }

        var opts = _options.CurrentValue;
        if (opts.Mode == AuthMode.Disabled)
        {
            return new LoginResult(true);
        }

        if (string.IsNullOrEmpty(opts.PasswordHash) || !PasswordHasher.Verify(password, opts.PasswordHash))
        {
            return new LoginResult(false, "Invalid credentials");
        }

        _limiter.Reset(ip);

        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, "operator"),
            new(ClaimTypes.AuthenticationMethod, opts.Mode.ToString()),
        };
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        await ctx.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties
            {
                IsPersistent = rememberMe,
                ExpiresUtc = rememberMe
                    ? DateTimeOffset.UtcNow.Add(opts.RememberMeDuration)
                    : DateTimeOffset.UtcNow.Add(opts.IdleTimeout),
                AllowRefresh = true,
            }).ConfigureAwait(false);

        return new LoginResult(true);
    }

    public Task LogoutAsync(HttpContext ctx)
        => ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
}

public sealed record LoginResult(bool Success, string? Error = null);
