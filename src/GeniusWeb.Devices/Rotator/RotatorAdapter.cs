using GeniusWeb.Contracts;
using GeniusWeb.Devices.Abstractions;
using Microsoft.Extensions.Logging;
using RotatorGenius.Communication;

namespace GeniusWeb.Devices.Rotator;

public sealed class RotatorAdapter : DeviceAdapterBase
{
    private readonly RotatorDevice _device;
    private readonly ILogger<RotatorAdapter> _logger;

    // The library raises its events on its own poll thread. Everything that decides whether
    // the adapter is running or stopped, and every state a reading or an error writes, goes
    // through this lock so a manual disconnect cannot interleave with them.
    private readonly object _linkGate = new();

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
        lock (_linkGate) MarkRunning();
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
        lock (_linkGate) MarkStopped();
        try { _device.Disconnect(); }
        catch (Exception ex) { _logger.LogWarning(ex, "Rotator {Id} disconnect", Identity.Id); }
        lock (_linkGate) State = DeviceConnectionState.Disconnected;
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

        lock (_linkGate)
        {
            if (IsStopped) return;
            // The fresh headings go out first, so no frame ever carries old headings under a
            // new state ...
            PublishStatus(payload);
            // ... and a status read is proof the link is up. The library follows a lost
            // controller by itself, and nothing else tells the adapter it is back after a
            // connection error marked it Faulted, so it would go on saying so while fresh
            // headings flowed. A no-op while already Connected, so one frame per reading.
            State = DeviceConnectionState.Connected;
        }
    }

    private void OnConnectionError(object? sender, RotatorErrorEventArgs e)
    {
        _logger.LogWarning("Rotator {Id} connection error: {Reason}", Identity.Id, e.Message);
        lock (_linkGate) State = DeviceConnectionState.Faulted;
    }

    public override ValueTask DisposeAsync()
    {
        _device.DataReceived -= OnDataReceived;
        _device.ConnectionError -= OnConnectionError;
        return ValueTask.CompletedTask;
    }
}
