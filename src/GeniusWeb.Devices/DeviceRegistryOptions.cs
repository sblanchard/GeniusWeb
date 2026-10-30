using GeniusWeb.Contracts;

namespace GeniusWeb.Devices;

public sealed class DeviceRegistryOptions
{
    public List<DeviceConfig> Devices { get; init; } = new();
}

public sealed class DeviceConfig
{
    public string Id { get; init; } = string.Empty;
    public DeviceKind Kind { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public string? Address { get; init; }
    public int? Port { get; init; }
}
