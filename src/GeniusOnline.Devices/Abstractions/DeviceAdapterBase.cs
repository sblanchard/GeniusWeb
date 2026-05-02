using GeniusOnline.Contracts;

namespace GeniusOnline.Devices.Abstractions;

public abstract class DeviceAdapterBase : IDeviceAdapter
{
    private DeviceConnectionState _state = DeviceConnectionState.Disconnected;
    private DeviceStatusFrame? _lastStatus;
    private bool _stopped;

    protected DeviceAdapterBase(DeviceIdentity identity)
    {
        Identity = identity;
    }

    public DeviceIdentity Identity { get; }

    /// <summary>True after MarkStopped(); blocks state transitions away from Disconnected
    /// and makes IsStopped-aware adapters short-circuit late status events from underlying
    /// client libraries that auto-reconnect.</summary>
    protected bool IsStopped => _stopped;

    /// <summary>Latch the adapter into a "manually disconnected" state. Until MarkRunning()
    /// is called, the State setter ignores Connecting/Connected/Discovering transitions so
    /// stray events from the inner client can't flip the UI back to Connected.</summary>
    protected void MarkStopped() => _stopped = true;

    /// <summary>Clear the manual-disconnect latch (called at the start of ConnectAsync).</summary>
    protected void MarkRunning() => _stopped = false;

    public DeviceConnectionState State
    {
        get => _state;
        protected set
        {
            if (_stopped && value != DeviceConnectionState.Disconnected) return;
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
