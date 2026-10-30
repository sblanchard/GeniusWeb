using GeniusWeb.Contracts;
using GeniusWeb.Devices.Abstractions;
using Microsoft.Extensions.Logging;
using RotatorGenius.Communication;

namespace GeniusWeb.Devices.Rotator;

public sealed class RotatorAdapter : DeviceAdapterBase
{
    private readonly RotatorDevice _device;
    private readonly ILogger<RotatorAdapter> _logger;

    public RotatorAdapter(DeviceIdentity identity, ILogger<RotatorAdapter> logger)
        : base(identity)
    {
        _logger = logger;
        _device = new RotatorDevice
        {
            Name = identity.DisplayName,
            Address = identity.Address ?? string.Empty,
            Port = identity.Port ?? 9006,
        };

        _device.DataReceived += OnDataReceived;
        _device.ConnectionError += OnConnectionError;
    }

    public override async Task ConnectAsync(CancellationToken ct = default)
    {
        MarkRunning();
        if (State == DeviceConnectionState.Connected) return;
        State = DeviceConnectionState.Connecting;

        try
        {
            await _device.ConnectAsync(ct).ConfigureAwait(false);
            State = DeviceConnectionState.Connected;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Rotator {Id} connect failed", Identity.Id);
            State = DeviceConnectionState.Faulted;
            throw;
        }
    }

    public override Task DisconnectAsync(CancellationToken ct = default)
    {
        MarkStopped();
        try { _device.Disconnect(); }
        catch (Exception ex) { _logger.LogWarning(ex, "Rotator {Id} disconnect", Identity.Id); }
        State = DeviceConnectionState.Disconnected;
        return Task.CompletedTask;
    }

    public override Task<DeviceCommandResult> ExecuteAsync(DeviceCommand command, CancellationToken ct = default)
    {
        try
        {
            // RotatorDevice's API is 0-indexed (it sends $"|P{rotatorIndex+1}"),
            // so rotator==0 means physical rotator 1 on the box.
            var rotatorIndex = AsInt(command.Args, "rotator", 0);
            // Not for "stop", which stops the whole controller and must never be argued with.
            if (command.Verb is "rotateTo" or "cw" or "ccw" && !IsKnownRotator(rotatorIndex, _device.Rotators.Count))
                return Task.FromResult(new DeviceCommandResult(false, $"No such rotator: {rotatorIndex + 1}"));
            switch (command.Verb)
            {
                case "rotateTo":
                {
                    var azimuth = AsInt(command.Args, "azimuth", -1);
                    if (azimuth < 0 || azimuth > 360)
                        return Task.FromResult(new DeviceCommandResult(false, "azimuth must be 0-360"));
                    _logger.LogDebug("Rotator {Id}: GoTo({Index}, {Azimuth})", Identity.Id, rotatorIndex, azimuth);
                    _device.GoTo(rotatorIndex, azimuth);
                    return Task.FromResult(new DeviceCommandResult(true));
                }
                case "cw":
                    _logger.LogDebug("Rotator {Id}: GoCW({Index})", Identity.Id, rotatorIndex);
                    _device.GoCW(rotatorIndex);
                    return Task.FromResult(new DeviceCommandResult(true));
                case "ccw":
                    _logger.LogDebug("Rotator {Id}: GoCCW({Index})", Identity.Id, rotatorIndex);
                    _device.GoCCW(rotatorIndex);
                    return Task.FromResult(new DeviceCommandResult(true));
                case "stop":
                    _logger.LogDebug("Rotator {Id}: Stop", Identity.Id);
                    _device.Stop();
                    return Task.FromResult(new DeviceCommandResult(true));
                default:
                    return Task.FromResult(new DeviceCommandResult(false, $"Unknown verb '{command.Verb}'"));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Rotator {Id} command {Verb} failed", Identity.Id, command.Verb);
            return Task.FromResult(new DeviceCommandResult(false, ex.Message));
        }
    }

    /// <summary>
    /// Whether a command may address this slot. Until a first status says how many there are
    /// (<paramref name="known"/> is 0), the controller is the judge of what it has.
    /// </summary>
    public static bool IsKnownRotator(int index, int known) =>
        index >= 0 && (known == 0 || index < known);

    private static int AsInt(IReadOnlyDictionary<string, object?>? args, string key, int fallback)
        => args is not null && args.TryGetValue(key, out var v) ? Convert.ToInt32(v) : fallback;

    private void OnDataReceived(object? sender, RotatorDataEventArgs e)
    {
        if (IsStopped) return;
        var payload = new Dictionary<string, object?>
        {
            ["isRotating"] = _device.IsRotating,
            ["rotators"] = _device.Rotators.Select(r => new
            {
                r.Number,
                r.Name,
                r.Azimuth,
                r.Target,
                r.Mode,
                r.Rotation,
                r.LimitCW,
                r.LimitCCW,
            }).ToArray(),
        };
        PublishStatus(payload);
    }

    private void OnConnectionError(object? sender, RotatorErrorEventArgs e)
    {
        _logger.LogWarning("Rotator {Id} connection error", Identity.Id);
        State = DeviceConnectionState.Faulted;
    }

    public override ValueTask DisposeAsync()
    {
        _device.DataReceived -= OnDataReceived;
        _device.ConnectionError -= OnConnectionError;
        return ValueTask.CompletedTask;
    }
}
