namespace GeniusOnline.Web.Auth;

public sealed class AuthOptions
{
    public AuthMode Mode { get; init; } = AuthMode.SinglePassword;

    public string? PasswordHash { get; init; }

    public TimeSpan IdleTimeout { get; init; } = TimeSpan.FromHours(8);

    public TimeSpan RememberMeDuration { get; init; } = TimeSpan.FromDays(30);
}

public enum AuthMode
{
    SinglePassword,
    MultiUser,
    Disabled,
}
