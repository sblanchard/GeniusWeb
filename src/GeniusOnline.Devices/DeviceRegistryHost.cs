using GeniusOnline.Contracts;
using GeniusOnline.Devices.Abstractions;
using GeniusOnline.Devices.Antenna;
using GeniusOnline.Devices.Power;
using GeniusOnline.Devices.Rotator;
using GeniusOnline.Devices.Tuner;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GeniusOnline.Devices;

public sealed class DeviceRegistryHost : BackgroundService
{
    private readonly DeviceRegistry _registry;
    private readonly DeviceRegistryOptions _options;
    private readonly IServiceProvider _services;
    private readonly ILogger<DeviceRegistryHost> _logger;

    public DeviceRegistryHost(
        DeviceRegistry registry,
        IOptions<DeviceRegistryOptions> options,
        IServiceProvider services,
        ILogger<DeviceRegistryHost> logger)
    {
        _registry = registry;
        _options = options.Value;
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        foreach (var cfg in _options.Devices)
        {
            try
            {
                var adapter = CreateAdapter(cfg);
                _registry.Register(adapter);
                _ = ConnectWithRetryAsync(adapter, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to register device {Id}", cfg.Id);
            }
        }

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Graceful shutdown — host signalled stop.
        }
    }

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
                // Loud once, then quiet — devices commonly aren't powered up at server start.
                if (attempt == 1)
                {
                    _logger.LogWarning(
                        "Connect failed for {Id} ({Reason}); will keep retrying in the background",
                        adapter.Identity.Id, ex.Message);
                }
                else
                {
                    _logger.LogDebug(
                        "Connect retry {Attempt} for {Id} failed ({Reason}); next in {Delay}s",
                        attempt, adapter.Identity.Id, ex.Message, delay.TotalSeconds);
                }

                try { await Task.Delay(delay, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { return; }
                delay = TimeSpan.FromSeconds(Math.Min(maxDelay.TotalSeconds, delay.TotalSeconds * 2));
            }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        foreach (var adapter in _registry.Adapters)
        {
            try { await adapter.DisconnectAsync(cancellationToken).ConfigureAwait(false); }
            catch (Exception ex) { _logger.LogWarning(ex, "Disconnect error for {Id}", adapter.Identity.Id); }
        }
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
    }
}
