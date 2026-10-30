using GeniusWeb.Contracts;

namespace GeniusWeb.Data.Entities;

public sealed class Preset
{
    public int Id { get; set; }
    public DeviceKind Kind { get; set; }
    public string DeviceId { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
