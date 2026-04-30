using GeniusOnline.Contracts;
using GeniusOnline.Devices;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GeniusOnline.Web.Hubs;

public sealed class DeviceHubBroadcaster : IHostedService
{
    private readonly DeviceRegistry _registry;
    private readonly IHubContext<DeviceHub, IDeviceHubClient> _hub;
    private readonly ILogger<DeviceHubBroadcaster> _logger;

    public DeviceHubBroadcaster(
        DeviceRegistry registry,
        IHubContext<DeviceHub, IDeviceHubClient> hub,
        ILogger<DeviceHubBroadcaster> logger)
    {
        _registry = registry;
        _hub = hub;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _registry.StatusChanged += OnStatusChanged;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _registry.StatusChanged -= OnStatusChanged;
        return Task.CompletedTask;
    }

    private void OnStatusChanged(object? sender, DeviceStatusFrame frame)
    {
        _ = SendAsync(frame);
    }

    private async Task SendAsync(DeviceStatusFrame frame)
    {
        try
        {
            await _hub.Clients.All.StatusChanged(frame).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to broadcast status for {Id}", frame.DeviceId);
        }
    }
}
