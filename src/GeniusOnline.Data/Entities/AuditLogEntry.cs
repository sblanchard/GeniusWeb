namespace GeniusOnline.Data.Entities;

public sealed class AuditLogEntry
{
    public long Id { get; set; }
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
    public string Category { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string? Detail { get; set; }
    public string? RemoteIp { get; set; }
    public string? UserAgent { get; set; }
}
