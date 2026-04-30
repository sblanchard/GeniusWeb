using GeniusOnline.Contracts;

namespace GeniusOnline.Devices.Abstractions;

public abstract class DeviceAdapterBase : IDeviceAdapter
{
    private DeviceConnectionState _state = DeviceConnectionState.Disconnected;
    private DeviceStatusFrame? _lastStatus;

    protected DeviceAdapterBase(DeviceIdentity identity)
    {
        Identity = identity;
    }

    public DeviceIdentity Identity { get; }

    public DeviceConnectionState State
    {
        get => _state;
        protected set
        {
            if (_state == value) return;
            _state = value;
            PublishStatus();
        }
    }

    public DeviceStatusFrame? LastStatus => _lastStatus;

    public event EventHandler<DeviceStatusFrame>? StatusChanged;

    public abstract Task ConnectAsync(CancellationToken ct = default);

    public abstract Task DisconnectAsync(CancellationToken ct = default);

    public abstract Task<DeviceCommandResult> ExecuteAsync(DeviceCommand command, CancellationToken ct = default);

    protected void PublishStatus(IReadOnlyDictionary<string, object?>? payload = null)
    {
        var frame = new DeviceStatusFrame(
            Identity.Id,
            Identity.Kind,
            _state,
            DateTimeOffset.UtcNow,
            payload ?? _lastStatus?.Payload ?? EmptyPayload);

        _lastStatus = frame;
        StatusChanged?.Invoke(this, frame);
    }

    protected static readonly IReadOnlyDictionary<string, object?> EmptyPayload =
        new Dictionary<string, object?>(0);

    public virtual ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
