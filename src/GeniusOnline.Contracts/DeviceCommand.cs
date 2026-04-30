namespace GeniusOnline.Contracts;

public sealed record DeviceCommand(
    string DeviceId,
    string Verb,
    IReadOnlyDictionary<string, object?>? Args = null);

public sealed record DeviceCommandResult(
    bool Success,
    string? Message = null,
    IReadOnlyDictionary<string, object?>? Data = null);
