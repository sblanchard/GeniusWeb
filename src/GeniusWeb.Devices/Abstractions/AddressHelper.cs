namespace GeniusWeb.Devices.Abstractions;

internal static class AddressHelper
{
    public static bool ShouldDiscover(string? address)
        => string.IsNullOrWhiteSpace(address)
           || string.Equals(address, "auto", StringComparison.OrdinalIgnoreCase)
           || string.Equals(address, "discover", StringComparison.OrdinalIgnoreCase);
}
