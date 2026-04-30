namespace GeniusOnline.Contracts;

public sealed record DeviceStatusFrame(
    string DeviceId,
    DeviceKind Kind,
    DeviceConnectionState Connection,
    DateTimeOffset Timestamp,
    IReadOnlyDictionary<string, object?> Payload);
