using GeniusOnline.Contracts;
using GeniusOnline.Devices.Abstractions;
using Microsoft.Extensions.Logging;
using TgDevice = SkySat.Signature4O3A.TunerGenius.Client.Device;
using TgDeviceInfo = SkySat.Signature4O3A.TunerGenius.Client.DeviceInfo;

namespace GeniusOnline.Devices.Tuner;

public sealed class TunerAdapter : DeviceAdapterBase
{
    // The TgLib's DeviceInfo hardcodes 9010 (the UDP discovery port). The actual TCP
    // control port for TGXL follows the FlexRadio convention: AG=9007, PG=9008, TG=9009.
    // We override discovery's 9010 → 9009 unless the user explicitly configures another port.
    private const int DefaultPort = 9009;

    private readonly ILogger<TunerAdapter> _logger;
    private TgDevice? _device;

    public TunerAdapter(DeviceIdentity identity, ILogger<TunerAdapter> logger)
        : base(identity)
    {
        _logger = logger;

        // The TgLib's IsCompatable check requires AppVersion.Major/Minor to match the
        // device firmware exactly. The default (assembly version 1.0.0.0) will fail
        // against a 1.2.x firmware, the worker exits after one loop, the socket gets
        // closed, and every command NREs inside SendMessage.
        // Pin to 1.2.0.0 by default (current TGXL firmware family); discovery refines
        // this from the live device when used.
        if (TgDevice.AppVersion is null || TgDevice.AppVersion.Major != 1 || TgDevice.AppVersion.Minor != 2)
        {
            TgDevice.AppVersion = new Version(1, 2, 0, 0);
        }
    }

    public override async Task ConnectAsync(CancellationToken ct = default)
    {
        MarkRunning();
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
            // info.Port is hardcoded to 9010 (UDP discovery). The TCP control port is 9009.
            port = (Identity.Port is int p && p > 0) ? p : DefaultPort;

            // Match the firmware's Major.Minor on TgDevice.AppVersion, otherwise
            // the lib's IsCompatable check fails, the worker exits after one loop,
            // and Client gets closed while Status remains Connected — every command
            // then NREs inside the lib's SendMessage.
            if (info.Version is not null)
            {
                TgDevice.AppVersion = new Version(info.Version.Major, info.Version.Minor, 0, 0);
                _logger.LogInformation("Tuner {Id} firmware {FW}; pinning AppVersion to {App}",
                    Identity.Id, info.Version, TgDevice.AppVersion);
            }

            _logger.LogInformation("Tuner {Id} discovered at {Host}:{Port} ({Name})",
                Identity.Id, address, port, info.Name);
            State = DeviceConnectionState.Connecting;
        }

        // The TgDevice library has no public Disconnect/Dispose, and Connect() is a no-op
        // when its internal worker is still running. So leave any prior _device's worker to
        // GC, and create a fresh client for every connect attempt.
        _device = new TgDevice(Identity.DisplayName, address!, port);
        _device.OnConnected += (_, _) => OnConnected();
        _device.OnStatusUpdated += (_, _) => OnStatus();
        _device.OnError += (_, msg) => OnError(msg);

        _logger.LogInformation("Tuner {Id} connecting to {Host}:{Port}", Identity.Id, address, port);
        _device.Connect();

        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (!_device.Connected && DateTimeOffset.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            await Task.Delay(200, ct).ConfigureAwait(false);
        }

        if (!_device.Connected)
        {
            State = DeviceConnectionState.Faulted;
            throw new TimeoutException(
                $"Tuner {Identity.Id}: connect timed out after 30s. " +
                "If the desktop Tuner Genius Utility is running, close it — TGXL only accepts one TCP control connection at a time.");
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
        MarkStopped();
        // TgDevice has no public Disconnect — null the reference so OnStatus/OnConnected
        // callbacks from the orphaned worker short-circuit, and the next ConnectAsync
        // creates a fresh client.
        _device = null;
        State = DeviceConnectionState.Disconnected;
        return Task.CompletedTask;
    }

    public override Task<DeviceCommandResult> ExecuteAsync(DeviceCommand command, CancellationToken ct = default)
    {
        if (_device is null || !_device.Connected)
        {
            _logger.LogWarning("Tuner {Id}: rejecting {Verb} — adapter not connected", Identity.Id, command.Verb);
            return Task.FromResult(new DeviceCommandResult(false, "Tuner adapter is not connected"));
        }

        _logger.LogInformation("Tuner {Id}: dispatching {Verb}", Identity.Id, command.Verb);

        try
        {
            SkySat.Signature4O3A.TunerGenius.Client.ResponseData? r = null;
            switch (command.Verb)
            {
                case "tune":
                    r = _device.Autotune();
                    break;
                case "bypass":
                    r = _device.SetBypass(AsBoolDefault(command.Args, "on", true));
                    break;
                case "operate":
                    r = _device.SetOperate(AsBoolDefault(command.Args, "on", true));
                    break;
                case "activate":
                    r = _device.Activate(AsInt(command.Args, "channel", 1));
                    break;
                case "selectAntenna":
                    r = _device.SetAntenna(AsInt(command.Args, "antenna", 0));
                    break;
                default:
                    return Task.FromResult(new DeviceCommandResult(false, $"Unknown verb '{command.Verb}'"));
            }

            if (r is null)
            {
                _logger.LogWarning("Tuner {Id}: {Verb} returned no response", Identity.Id, command.Verb);
                return Task.FromResult(new DeviceCommandResult(false, "Device returned no response"));
            }

            _logger.LogInformation("Tuner {Id}: {Verb} → ErrorCode={Err} Data={Data}",
                Identity.Id, command.Verb, r.ErrorCode, r.Data ?? "");

            return Task.FromResult(r.ErrorCode == 0
                ? new DeviceCommandResult(true)
                : new DeviceCommandResult(false, $"Device error {r.ErrorCode}"));
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

        // Idle sentinels: device reports a noise-floor watt count and -60dB return loss
        // when no RF is present. Hide them — match the desktop UI's behaviour.
        var transmitting = s.PTTA || s.PTTB;
        var pwr = transmitting ? (double)s.FWD : 0.0;
        var swr = transmitting ? Math.Max(1.0, (double)s.SWR) : 1.0;

        var payload = new Dictionary<string, object?>
        {
            ["operate"] = s.Operate,
            ["tuning"] = s.Tuning,
            ["bypass"] = s.Bypass,
            ["forwardW"] = pwr,
            ["forwardPeakW"] = transmitting ? (double)s.FWD_Peak : 0.0,
            ["swrIn"] = swr,
            ["swrOut"] = swr,
            ["bandA"] = s.BandA.ToString(),
            ["bandB"] = s.BandB.ToString(),
            ["freqA"] = (double)s.FreqA,
            ["freqB"] = (double)s.FreqB,
            ["flexA"] = s.FlexA,
            ["flexB"] = s.FlexB,
            ["pttA"] = s.PTTA,
            ["pttB"] = s.PTTB,
            ["bypassA"] = s.BypassA,
            ["bypassB"] = s.BypassB,
            ["active"] = s.Active,
            ["l"] = (double)s.RelayL,
            ["c1"] = (double)s.RelayC1,
            ["c2"] = (double)s.RelayC2,
            ["error"] = string.IsNullOrWhiteSpace(s.Error) ? null : s.Error,
        };
        PublishStatus(payload);
    }

    private void OnError(string message)
    {
        _logger.LogWarning("Tuner {Id} error: {Message}", Identity.Id, message);
    }

    private static int AsInt(IReadOnlyDictionary<string, object?>? args, string key, int fallback)
        => args is not null && args.TryGetValue(key, out var v) ? Convert.ToInt32(v) : fallback;

    private static bool AsBoolDefault(IReadOnlyDictionary<string, object?>? args, string key, bool fallback)
        => args is not null && args.TryGetValue(key, out var v) ? Convert.ToBoolean(v) : fallback;

    public override ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
