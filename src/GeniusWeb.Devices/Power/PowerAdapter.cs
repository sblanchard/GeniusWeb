using GeniusWeb.Contracts;
using GeniusWeb.Devices.Abstractions;
using Microsoft.Extensions.Logging;
using PowerGenius.Core.Services;
using PgBands = SkySat.Signature4O3A.PowerGenius.Client.Bands;
using PgDevice = SkySat.Signature4O3A.PowerGenius.Client.Device;

namespace GeniusWeb.Devices.Power;

public sealed class PowerAdapter : DeviceAdapterBase
{
    private const int DefaultPort = 9007;

    private readonly ILogger<PowerAdapter> _logger;
    private IDeviceClient? _client;

    public PowerAdapter(DeviceIdentity identity, ILogger<PowerAdapter> logger)
        : base(identity)
    {
        _logger = logger;
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
                throw new TimeoutException($"Power {Identity.Id}: no device responded to discovery");
            }
            address = info.Address;
            port = info.Port == 0 ? DefaultPort : info.Port;
            _logger.LogInformation("Power {Id} discovered at {Host}:{Port} ({Name})",
                Identity.Id, address, port, info.Name);
            State = DeviceConnectionState.Connecting;
        }

        _client?.Disconnect();
        _client = new PgDeviceClient(Identity.DisplayName, address!, port);
        _client.StatusUpdated += OnStatusUpdated;

        await Task.Run(_client.Connect, ct).ConfigureAwait(false);

        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        while (!_client.Connected && DateTimeOffset.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            await Task.Delay(200, ct).ConfigureAwait(false);
        }

        if (!_client.Connected)
        {
            State = DeviceConnectionState.Faulted;
            throw new TimeoutException($"Power {Identity.Id}: timed out waiting for first status");
        }

        State = DeviceConnectionState.Connected;
    }

    private static Task<SkySat.Signature4O3A.PowerGenius.Client.DeviceInfo?> DiscoverAsync(
        TimeSpan timeout, CancellationToken ct)
    {
        // PgDevice.Discover blocks for up to ~3s scanning UDP and accumulating
        // results into a static list. Run it on a worker so we can still respect ct.
        return Task.Run(() =>
        {
            try
            {
                PgDevice.InitDiscover();
                var found = PgDevice.Discover((int)Math.Min(timeout.TotalMilliseconds, 5000));
                return found.Length > 0
                    ? (SkySat.Signature4O3A.PowerGenius.Client.DeviceInfo?)found[0]
                    : null;
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
        try { _client?.Disconnect(); }
        catch (Exception ex) { _logger.LogWarning(ex, "Power {Id} disconnect", Identity.Id); }
        State = DeviceConnectionState.Disconnected;
        return Task.CompletedTask;
    }

    public override Task<DeviceCommandResult> ExecuteAsync(DeviceCommand command, CancellationToken ct = default)
    {
        if (_client is null)
            return Task.FromResult(new DeviceCommandResult(false, "Power adapter is not connected"));

        try
        {
            switch (command.Verb)
            {
                case "setOperate":
                    _client.SetOperate(AsBool(command.Args, "on"));
                    return Task.FromResult(new DeviceCommandResult(true));
                case "clearFault":
                    _client.ResetAlarm();
                    return Task.FromResult(new DeviceCommandResult(true));
                case "setBand":
                    _client.SetBand(AsString(command.Args, "port"), ParseBand(AsString(command.Args, "band")));
                    return Task.FromResult(new DeviceCommandResult(true));
                case "setSource":
                    _client.SetSource(AsString(command.Args, "port"), AsString(command.Args, "source"));
                    return Task.FromResult(new DeviceCommandResult(true));
                case "setFanMode":
                    _client.SetFanMode(AsString(command.Args, "mode"));
                    return Task.FromResult(new DeviceCommandResult(true));
                default:
                    return Task.FromResult(new DeviceCommandResult(false, $"Unknown verb '{command.Verb}'"));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Power {Id} command {Verb} failed", Identity.Id, command.Verb);
            return Task.FromResult(new DeviceCommandResult(false, ex.Message));
        }
    }

    private bool _loggedFirstStatus;

    private void OnStatusUpdated(object? sender, EventArgs e)
    {
        if (_client is null || IsStopped) return;
        var s = _client.Status;

        if (!_loggedFirstStatus)
        {
            _loggedFirstStatus = true;
            _logger.LogInformation(
                "Power {Id} first status: PeakPWR={Pwr}W PeakSWR={Swr} I={Cur}A V={Vdd}V VAC={Vac}V T={T}°C HT={HT}°C op={Op} bandA={BandA} fault={Fault}",
                Identity.Id, s.PeakPWR, s.PeakSWR, s.Current, s.Voltage, s.VoltageAC,
                s.Temperature, s.HarmonicTemperature, s.Operate, s.BandA, s.Error);
        }

        // pglib returns PeakPWR=35 and PeakSWR=30 as idle-state sentinels when neither
        // PTT is active (see pglib/Device.cs lines 452/458). Convert those to 0/1.0 for display.
        var transmitting = s.PTTA || s.PTTB;
        var pwr = transmitting ? (double)s.PeakPWR : 0.0;
        var swr = transmitting ? Math.Max(1.0, (double)s.PeakSWR) : 1.0;

        var payload = new Dictionary<string, object?>
        {
            ["operate"] = s.Operate,
            ["serial"] = _client.SerialNumber,
            ["firmware"] = _client.Version?.ToString(),
            ["forwardW"] = pwr,
            ["swr"] = swr,
            ["voltage"] = (double)s.Voltage,
            ["voltageAC"] = (double)s.VoltageAC,
            ["current"] = (double)s.Current,
            ["tempC"] = (double)s.Temperature,
            ["harmonicTempC"] = (double)s.HarmonicTemperature,
            ["fault"] = string.IsNullOrWhiteSpace(s.Error) ? null : s.Error,
            ["bandA"] = s.BandA.ToString(),
            ["bandB"] = s.BandB.ToString(),
            ["freqA"] = (double)s.CAT1,
            ["freqB"] = (double)s.CAT2,
            ["flexA"] = s.FlexRadioA,
            ["flexB"] = s.FlexRadioB,
            ["pttA"] = s.PTTA,
            ["pttB"] = s.PTTB,
            ["foldback"] = s.Foldback,
            ["apc"] = s.APC,
        };

        if (_client.Connected && State != DeviceConnectionState.Connected)
            State = DeviceConnectionState.Connected;

        PublishStatus(payload);
    }

    private static string AsString(IReadOnlyDictionary<string, object?>? args, string key)
        => args is not null && args.TryGetValue(key, out var v) ? v?.ToString() ?? string.Empty : string.Empty;

    private static bool AsBool(IReadOnlyDictionary<string, object?>? args, string key)
        => args is not null && args.TryGetValue(key, out var v) && Convert.ToBoolean(v);

    private static PgBands ParseBand(string s) => s.ToLowerInvariant() switch
    {
        "160m" => PgBands.Band_160m,
        "80m" => PgBands.Band_80m,
        "40m" => PgBands.Band_40m,
        "30m" => PgBands.Band_30m,
        "20m" => PgBands.Band_20m,
        "17m" => PgBands.Band_17m,
        "15m" => PgBands.Band_15m,
        "12m" => PgBands.Band_12m,
        "10m" => PgBands.Band_10m,
        "6m" => PgBands.Band_6m,
        _ => PgBands.Band_None,
    };

    public override ValueTask DisposeAsync()
    {
        if (_client is not null)
        {
            _client.StatusUpdated -= OnStatusUpdated;
            try { _client.Disconnect(); } catch { }
        }
        return ValueTask.CompletedTask;
    }
}
