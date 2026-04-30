using GeniusOnline.Contracts;

namespace GeniusOnline.Devices.Abstractions;

public interface IDeviceAdapter : IAsyncDisposable
{
    DeviceIdentity Identity { get; }

    DeviceConnectionState State { get; }

    DeviceStatusFrame? LastStatus { get; }

    event EventHandler<DeviceStatusFrame>? StatusChanged;

    Task ConnectAsync(CancellationToken ct = default);

    Task DisconnectAsync(CancellationToken ct = default);

    Task<DeviceCommandResult> ExecuteAsync(DeviceCommand command, CancellationToken ct = default);
}
