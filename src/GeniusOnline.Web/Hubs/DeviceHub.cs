using GeniusOnline.Contracts;
using GeniusOnline.Devices;
using Microsoft.AspNetCore.SignalR;

namespace GeniusOnline.Web.Hubs;

public sealed class DeviceHub : Hub<IDeviceHubClient>
{
    private readonly DeviceRegistry _registry;

    public DeviceHub(DeviceRegistry registry)
    {
        _registry = registry;
    }

    public override async Task OnConnectedAsync()
    {
        await base.OnConnectedAsync().ConfigureAwait(false);
        foreach (var adapter in _registry.Adapters)
        {
            if (adapter.LastStatus is { } status)
            {
                await Clients.Caller.StatusChanged(status).ConfigureAwait(false);
            }
        }
    }

    public Task<DeviceCommandResult> Execute(DeviceCommand command)
    {
        var adapter = _registry.Get(command.DeviceId);
        if (adapter is null)
        {
            return Task.FromResult(new DeviceCommandResult(false, $"Unknown device {command.DeviceId}"));
        }
        return adapter.ExecuteAsync(command, Context.ConnectionAborted);
    }
}

public interface IDeviceHubClient
{
    Task StatusChanged(DeviceStatusFrame frame);
}
