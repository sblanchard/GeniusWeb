using GeniusWeb.Contracts;
using GeniusWeb.Devices.Abstractions;
using GeniusWeb.Devices.Antenna;
using GeniusWeb.Devices.Power;
using GeniusWeb.Devices.Rotator;
using GeniusWeb.Devices.Tuner;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GeniusWeb.Devices;

public sealed class DeviceRegistryHost : BackgroundService
{
    private readonly DeviceRegistry _registry;
    private readonly IServiceProvider _services;
    private readonly ILogger<DeviceRegistryHost> _logger;
    private readonly Dictionary<string, CancellationTokenSource> _adapterLifetimes = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CancellationToken _hostStopping;

    public DeviceRegistryHost(
        DeviceRegistry registry,
        IServiceProvider services,
        ILogger<DeviceRegistryHost> logger)
    {
        _registry = registry;
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _hostStopping = stoppingToken;
        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    /// <summary>
    /// Replace the current set of registered adapters with the given configuration.
    /// Existing adapters whose config is unchanged are kept; removed entries are disposed;
    /// new entries are registered and connected.
    /// </summary>
    public async Task ApplyConfigAsync(IEnumerable<DeviceConfig> devices, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var desired = devices.ToDictionary(d => d.Id, d => d, StringComparer.Ordinal);
            var current = _registry.Adapters.ToDictionary(a => a.Identity.Id, a => a, StringComparer.Ordinal);

            // Remove or replace
            foreach (var (id, adapter) in current)
            {
                desired.TryGetValue(id, out var newCfg);
                var keep = newCfg is not null && SameConfig(adapter.Identity, newCfg);
                if (keep) continue;

                _logger.LogInformation("Removing adapter {Id}", id);
                if (_adapterLifetimes.TryGetValue(id, out var cts)) { cts.Cancel(); cts.Dispose(); _adapterLifetimes.Remove(id); }
                await _registry.UnregisterAsync(id).ConfigureAwait(false);
            }

            // Add or replace
            foreach (var (id, cfg) in desired)
            {
                if (_registry.Get(id) is not null) continue; // unchanged kept above

                try
                {
                    var adapter = CreateAdapter(cfg);
                    _registry.Register(adapter);
                    var cts = CancellationTokenSource.CreateLinkedTokenSource(_hostStopping);
                    _adapterLifetimes[id] = cts;
                    _ = ConnectWithRetryAsync(adapter, cts.Token);
                    _logger.LogInformation("Registered adapter {Id} ({Kind} @ {Address})", id, cfg.Kind, cfg.Address ?? "auto");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to register device {Id}", id);
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Stop the adapter's connect-retry loop and disconnect it. Adapter stays registered.</summary>
    public async Task DisconnectAsync(string deviceId, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_adapterLifetimes.TryGetValue(deviceId, out var cts))
            {
                cts.Cancel();
                cts.Dispose();
                _adapterLifetimes.Remove(deviceId);
            }
            var adapter = _registry.Get(deviceId);
            if (adapter is not null)
            {
                try { await adapter.DisconnectAsync(ct).ConfigureAwait(false); }
                catch (Exception ex) { _logger.LogWarning(ex, "Disconnect error for {Id}", deviceId); }
            }
        }
        finally { _gate.Release(); }
    }

    /// <summary>Restart the connect-retry loop for an already-registered adapter.</summary>
    public async Task ReconnectAsync(string deviceId, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var adapter = _registry.Get(deviceId);
            if (adapter is null) return;

            if (_adapterLifetimes.TryGetValue(deviceId, out var existing))
            {
                existing.Cancel();
                existing.Dispose();
                _adapterLifetimes.Remove(deviceId);
            }

            var cts = CancellationTokenSource.CreateLinkedTokenSource(_hostStopping);
            _adapterLifetimes[deviceId] = cts;
            _ = ConnectWithRetryAsync(adapter, cts.Token);
        }
        finally { _gate.Release(); }
    }

    private static bool SameConfig(DeviceIdentity i, DeviceConfig c)
        => i.Kind == c.Kind
           && string.Equals(i.DisplayName, c.DisplayName, StringComparison.Ordinal)
           && string.Equals(i.Address, c.Address, StringComparison.Ordinal)
           && i.Port == c.Port;

    private IDeviceAdapter CreateAdapter(DeviceConfig cfg)
    {
        var identity = new DeviceIdentity(cfg.Id, cfg.Kind, cfg.DisplayName, cfg.Address, cfg.Port);
        var loggerFactory = (ILoggerFactory)_services.GetService(typeof(ILoggerFactory))!;

        return cfg.Kind switch
        {
            DeviceKind.Rotator => new RotatorAdapter(identity, loggerFactory.CreateLogger<RotatorAdapter>()),
            DeviceKind.Antenna => new AntennaAdapter(identity, loggerFactory.CreateLogger<AntennaAdapter>()),
            DeviceKind.Power => new PowerAdapter(identity, loggerFactory.CreateLogger<PowerAdapter>()),
            DeviceKind.Tuner => new TunerAdapter(identity, loggerFactory.CreateLogger<TunerAdapter>()),
            _ => throw new ArgumentOutOfRangeException(nameof(cfg.Kind)),
        };
    }

    private async Task ConnectWithRetryAsync(IDeviceAdapter adapter, CancellationToken ct)
    {
        var delay = TimeSpan.FromSeconds(2);
        var maxDelay = TimeSpan.FromSeconds(60);
        var attempt = 0;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await adapter.ConnectAsync(ct).ConfigureAwait(false);
                if (attempt > 0)
                    _logger.LogInformation("Connected to {Id} after {Attempts} attempts", adapter.Identity.Id, attempt);
                return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                attempt++;
                if (attempt == 1)
                    _logger.LogWarning("Connect failed for {Id} ({Reason}); will keep retrying", adapter.Identity.Id, ex.Message);
                else
                    _logger.LogDebug("Retry {Attempt} for {Id} failed ({Reason}); next in {Delay}s",
                        attempt, adapter.Identity.Id, ex.Message, delay.TotalSeconds);

                try { await Task.Delay(delay, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { return; }
                delay = TimeSpan.FromSeconds(Math.Min(maxDelay.TotalSeconds, delay.TotalSeconds * 2));
            }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        foreach (var cts in _adapterLifetimes.Values) { try { cts.Cancel(); } catch { } }
        foreach (var adapter in _registry.Adapters)
        {
            try { await adapter.DisconnectAsync(cancellationToken).ConfigureAwait(false); }
            catch (Exception ex) { _logger.LogWarning(ex, "Disconnect error for {Id}", adapter.Identity.Id); }
        }
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
    }
}
