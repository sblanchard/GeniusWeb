namespace GeniusOnline.Contracts;

public sealed record DeviceIdentity(
    string Id,
    DeviceKind Kind,
    string DisplayName,
    string? Address,
    int? Port);
