using GeniusOnline.Contracts;
using GeniusOnline.Devices.Abstractions;
using Microsoft.Extensions.Logging;
using TgDevice = SkySat.Signature4O3A.TunerGenius.Client.Device;
using TgDeviceInfo = SkySat.Signature4O3A.TunerGenius.Client.DeviceInfo;

namespace GeniusOnline.Devices.Tuner;

public sealed class TunerAdapter : DeviceAdapterBase
{
    private const int DefaultPort = 9010;

    private readonly ILogger<TunerAdapter> _logger;
    private TgDevice? _device;

    public TunerAdapter(DeviceIdentity identity, ILogger<TunerAdapter> logger)
        : base(identity)
    {
        _logger = logger;
    }

    public override async Task ConnectAsync(CancellationToken ct = default)
    {
        State = DeviceConnectionState.Connecting;

        var address = Identity.Address;
        var port = Identity.Port ?? DefaultPort;

        if (AddressHelper.ShouldDiscover(address))
        {
            State = DeviceConnectionState.Discovering;
            var info = await DiscoverAsync(TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);
            if (info is null)
            {
                State = DeviceConnectionState.Faulted;
                throw new TimeoutException($"Tuner {Identity.Id}: no device responded to discovery");
            }
            address = info.Address;
            port = info.Port == 0 ? DefaultPort : info.Port;
            _logger.LogInformation("Tuner {Id} discovered at {Host}:{Port} ({Name})",
                Identity.Id, address, port, info.Name);
            State = DeviceConnectionState.Connecting;
        }

        _device = new TgDevice(Identity.DisplayName, address!, port);
        _device.OnConnected += (_, _) => OnConnected();
        _device.OnStatusUpdated += (_, _) => OnStatus();
        _device.OnError += (_, msg) => OnError(msg);

        _device.Connect();

        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        while (!_device.Connected && DateTimeOffset.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            await Task.Delay(200, ct).ConfigureAwait(false);
        }

        if (!_device.Connected)
        {
            State = DeviceConnectionState.Faulted;
            throw new TimeoutException($"Tuner {Identity.Id}: connect timed out");
        }

        State = DeviceConnectionState.Connected;
    }

    private static Task<TgDeviceInfo?> DiscoverAsync(TimeSpan timeout, CancellationToken ct)
    {
        return Task.Run(() =>
        {
            try
            {
                TgDevice.InitDiscover();
                var found = TgDevice.Discover((int)Math.Min(timeout.TotalMilliseconds, 5000));
                return found.Length > 0 ? found[0] : null;
            }
            catch
            {
                return null;
            }
        }, ct);
    }

    public override Task DisconnectAsync(CancellationToken ct = default)
    {
        State = DeviceConnectionState.Disconnected;
        return Task.CompletedTask;
    }

    public override Task<DeviceCommandResult> ExecuteAsync(DeviceCommand command, CancellationToken ct = default)
    {
        if (_device is null)
            return Task.FromResult(new DeviceCommandResult(false, "Tuner adapter is not connected"));

        try
        {
            switch (command.Verb)
            {
                case "tune":
                    _device.Autotune();
                    return Task.FromResult(new DeviceCommandResult(true));
                case "bypass":
                    _device.SetBypass(AsBoolDefault(command.Args, "on", true));
                    return Task.FromResult(new DeviceCommandResult(true));
                case "operate":
                    _device.SetOperate(AsBoolDefault(command.Args, "on", true));
                    return Task.FromResult(new DeviceCommandResult(true));
                case "activate":
                    _device.Activate(AsInt(command.Args, "channel", 1));
                    return Task.FromResult(new DeviceCommandResult(true));
                case "selectAntenna":
                    _device.SetAntenna(AsInt(command.Args, "antenna", 0));
                    return Task.FromResult(new DeviceCommandResult(true));
                default:
                    return Task.FromResult(new DeviceCommandResult(false, $"Unknown verb '{command.Verb}'"));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Tuner {Id} command {Verb} failed", Identity.Id, command.Verb);
            return Task.FromResult(new DeviceCommandResult(false, ex.Message));
        }
    }

    private void OnConnected()
    {
        if (State != DeviceConnectionState.Connected)
            State = DeviceConnectionState.Connected;
    }

    private void OnStatus()
    {
        if (_device is null) return;
        var s = _device.Status;
        var payload = new Dictionary<string, object?>
        {
            ["operate"] = s.Operate,
            ["tuning"] = s.Tuning,
            ["bypass"] = s.Bypass,
            ["forwardW"] = (double)s.FWD,
            ["forwardPeakW"] = (double)s.FWD_Peak,
            ["swrIn"] = (double)s.SWR,
            ["swrOut"] = (double)s.SWR,
            ["bandA"] = s.BandA.ToString(),
            ["bandB"] = s.BandB.ToString(),
            ["pttA"] = s.PTTA,
            ["pttB"] = s.PTTB,
            ["bypassA"] = s.BypassA,
            ["bypassB"] = s.BypassB,
            ["active"] = s.Active,
            ["l"] = (double)s.RelayL,
            ["c"] = (double)(s.RelayC1 + s.RelayC2),
            ["error"] = string.IsNullOrWhiteSpace(s.Error) ? null : s.Error,
        };
        PublishStatus(payload);
    }

    private void OnError(string message)
    {
        _logger.LogDebug("Tuner {Id} error: {Message}", Identity.Id, message);
    }

    private static int AsInt(IReadOnlyDictionary<string, object?>? args, string key, int fallback)
        => args is not null && args.TryGetValue(key, out var v) ? Convert.ToInt32(v) : fallback;

    private static bool AsBoolDefault(IReadOnlyDictionary<string, object?>? args, string key, bool fallback)
        => args is not null && args.TryGetValue(key, out var v) ? Convert.ToBoolean(v) : fallback;

    public override ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
