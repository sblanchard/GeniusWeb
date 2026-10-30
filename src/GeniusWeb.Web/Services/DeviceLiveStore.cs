using System.Collections.Concurrent;
using GeniusWeb.Contracts;
using GeniusWeb.Devices;

namespace GeniusWeb.Web.Services;

public sealed class DeviceLiveStore
{
    private readonly ConcurrentDictionary<string, DeviceStatusFrame> _frames = new();
    private readonly DeviceRegistry _registry;

    public DeviceLiveStore(DeviceRegistry registry)
    {
        _registry = registry;
        _registry.StatusChanged += (_, frame) =>
        {
            _frames[frame.DeviceId] = frame;
            Updated?.Invoke(this, frame);
        };

        foreach (var adapter in _registry.Adapters)
        {
            if (adapter.LastStatus is { } f) _frames[f.DeviceId] = f;
        }
    }

    public event EventHandler<DeviceStatusFrame>? Updated;

    public IReadOnlyCollection<DeviceIdentity> KnownDevices
        => _registry.Adapters.Select(a => a.Identity).ToArray();

    public DeviceStatusFrame? Get(string deviceId)
        => _frames.TryGetValue(deviceId, out var f) ? f : null;

    public IEnumerable<DeviceStatusFrame> All() => _frames.Values;

    public Task<DeviceCommandResult> ExecuteAsync(DeviceCommand command, CancellationToken ct = default)
    {
        var adapter = _registry.Get(command.DeviceId);
        return adapter is null
            ? Task.FromResult(new DeviceCommandResult(false, $"Unknown device {command.DeviceId}"))
            : adapter.ExecuteAsync(command, ct);
    }
}
