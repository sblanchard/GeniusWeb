using System.Collections.Concurrent;
using GeniusWeb.Contracts;
using GeniusWeb.Devices.Abstractions;

namespace GeniusWeb.Devices;

public sealed class DeviceRegistry
{
    private readonly ConcurrentDictionary<string, IDeviceAdapter> _adapters = new();

    public event EventHandler<DeviceStatusFrame>? StatusChanged;

    public IReadOnlyCollection<IDeviceAdapter> Adapters => _adapters.Values.ToArray();

    public IDeviceAdapter? Get(string deviceId)
        => _adapters.TryGetValue(deviceId, out var a) ? a : null;

    public void Register(IDeviceAdapter adapter)
    {
        if (!_adapters.TryAdd(adapter.Identity.Id, adapter))
            throw new InvalidOperationException($"Device {adapter.Identity.Id} already registered");

        adapter.StatusChanged += OnAdapterStatusChanged;
    }

    public async Task UnregisterAsync(string deviceId)
    {
        if (_adapters.TryRemove(deviceId, out var adapter))
        {
            adapter.StatusChanged -= OnAdapterStatusChanged;
            await adapter.DisposeAsync().ConfigureAwait(false);
        }
    }

    private void OnAdapterStatusChanged(object? sender, DeviceStatusFrame frame)
        => StatusChanged?.Invoke(this, frame);
}
