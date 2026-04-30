using System.Net;
using AntennaGenius.Core.Models;
using AntennaGenius.Core.Protocol;
using AntennaGenius.Core.Services;
using GeniusOnline.Contracts;
using GeniusOnline.Devices.Abstractions;
using Microsoft.Extensions.Logging;

namespace GeniusOnline.Devices.Antenna;

public sealed class AntennaAdapter : DeviceAdapterBase
{
    private const int DefaultPort = 9007;

    private readonly DeviceService _service = new();
    private readonly ILogger<AntennaAdapter> _logger;

    public AntennaAdapter(DeviceIdentity identity, ILogger<AntennaAdapter> logger)
        : base(identity)
    {
        _logger = logger;

        _service.ConnectionStateChanged += OnConnectionStateChanged;
        _service.DeviceUpdated += _ => PublishCurrent();
        _service.PortUpdated += _ => PublishCurrent();
        _service.OutputUpdated += _ => PublishCurrent();
    }

    public override async Task ConnectAsync(CancellationToken ct = default)
    {
        State = DeviceConnectionState.Connecting;

        string host;
        int port = Identity.Port ?? DefaultPort;

        if (AddressHelper.ShouldDiscover(Identity.Address))
        {
            State = DeviceConnectionState.Discovering;
            var discovered = await DiscoverAsync(TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);
            if (discovered is null)
            {
                State = DeviceConnectionState.Faulted;
                throw new TimeoutException($"Antenna {Identity.Id}: no device responded to discovery");
            }
            host = discovered.IpAddress.ToString();
            port = discovered.Port == 0 ? DefaultPort : discovered.Port;
            _logger.LogInformation("Antenna {Id} discovered at {Host}:{Port} ({Name})",
                Identity.Id, host, port, discovered.Name);
            State = DeviceConnectionState.Connecting;
        }
        else
        {
            host = Identity.Address!;
        }

        try
        {
            await _service.ConnectAsync(host, port, ct).ConfigureAwait(false);
        }
        catch
        {
            State = DeviceConnectionState.Faulted;
            throw;
        }
    }

    private static async Task<DiscoveredDevice?> DiscoverAsync(TimeSpan timeout, CancellationToken ct)
    {
        using var discovery = new AgDiscovery();
        var tcs = new TaskCompletionSource<DiscoveredDevice>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnFound(DiscoveredDevice d) => tcs.TrySetResult(d);
        discovery.DeviceDiscovered += OnFound;

        try
        {
            discovery.StartListening();
            await discovery.SendDiscoveryBroadcastAsync(ct).ConfigureAwait(false);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout);
            using (cts.Token.Register(() => tcs.TrySetResult(null!)))
            {
                return await tcs.Task.ConfigureAwait(false);
            }
        }
        finally
        {
            discovery.DeviceDiscovered -= OnFound;
        }
    }

    public override async Task DisconnectAsync(CancellationToken ct = default)
    {
        await _service.DisconnectAsync().ConfigureAwait(false);
        State = DeviceConnectionState.Disconnected;
    }

    public override async Task<DeviceCommandResult> ExecuteAsync(DeviceCommand command, CancellationToken ct = default)
    {
        try
        {
            switch (command.Verb)
            {
                case "selectAntenna":
                {
                    var port = AsInt(command.Args, "port");
                    var antenna = AsInt(command.Args, "antenna");
                    await _service.SelectAntennaAsync(port, antenna, ct).ConfigureAwait(false);
                    return new DeviceCommandResult(true);
                }
                case "setBand":
                {
                    var port = AsInt(command.Args, "port");
                    var band = ParseBand(AsString(command.Args, "band"));
                    await _service.SetBandAsync(port, band, ct).ConfigureAwait(false);
                    return new DeviceCommandResult(true);
                }
                case "setRelay":
                {
                    var relay = AsInt(command.Args, "relay");
                    var on = AsBool(command.Args, "on");
                    await _service.SetRelayAsync(relay, on, ct).ConfigureAwait(false);
                    return new DeviceCommandResult(true);
                }
                case "raw":
                    await _service.SendCommandAsync(AsString(command.Args, "cmd"), ct).ConfigureAwait(false);
                    return new DeviceCommandResult(true);
                default:
                    return new DeviceCommandResult(false, $"Unknown verb '{command.Verb}'");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Antenna {Id} command {Verb} failed", Identity.Id, command.Verb);
            return new DeviceCommandResult(false, ex.Message);
        }
    }

    private void OnConnectionStateChanged(ConnectionState state)
    {
        State = state switch
        {
            ConnectionState.Connecting => DeviceConnectionState.Connecting,
            ConnectionState.Connected => DeviceConnectionState.Connected,
            ConnectionState.Reconnecting => DeviceConnectionState.Connecting,
            ConnectionState.Error => DeviceConnectionState.Faulted,
            _ => DeviceConnectionState.Disconnected,
        };
    }

    private void PublishCurrent()
    {
        var d = _service.Device;
        var payload = new Dictionary<string, object?>
        {
            ["name"] = d.Name,
            ["serial"] = d.SerialNumber,
            ["firmware"] = d.FirmwareVersion,
            ["ports"] = d.Ports.Select(p => new
            {
                index = p.Index,
                name = p.Name,
                band = p.CurrentBand.ToString(),
                rxAntenna = p.RxAntennaIndex,
                txAntenna = p.TxAntennaIndex,
                frequency = p.Frequency,
                source = p.BandSource.ToString(),
            }).ToArray(),
            ["antennas"] = d.Antennas.Select(a => new { a.Index, a.Name }).ToArray(),
        };
        PublishStatus(payload);
    }

    private static int AsInt(IReadOnlyDictionary<string, object?>? args, string key)
        => args is not null && args.TryGetValue(key, out var v) ? Convert.ToInt32(v) : 0;

    private static string AsString(IReadOnlyDictionary<string, object?>? args, string key)
        => args is not null && args.TryGetValue(key, out var v) ? v?.ToString() ?? string.Empty : string.Empty;

    private static bool AsBool(IReadOnlyDictionary<string, object?>? args, string key)
        => args is not null && args.TryGetValue(key, out var v) && Convert.ToBoolean(v);

    private static PortBandType ParseBand(string s) =>
        s.ToLowerInvariant() switch
        {
            "160m" => PortBandType.Band160m,
            "80m" => PortBandType.Band80m,
            "60m" => PortBandType.Band60m,
            "40m" => PortBandType.Band40m,
            "30m" => PortBandType.Band30m,
            "20m" => PortBandType.Band20m,
            "17m" => PortBandType.Band17m,
            "15m" => PortBandType.Band15m,
            "12m" => PortBandType.Band12m,
            "10m" => PortBandType.Band10m,
            "6m" => PortBandType.Band6m,
            "4m" => PortBandType.Band4m,
            "2m" => PortBandType.Band2m,
            "70cm" => PortBandType.Band70cm,
            _ => PortBandType.None,
        };

    public override async ValueTask DisposeAsync()
    {
        _service.ConnectionStateChanged -= OnConnectionStateChanged;
        await _service.DisposeAsync().ConfigureAwait(false);
    }
}
