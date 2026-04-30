using GeniusOnline.Contracts;
using GeniusOnline.Devices;
using GeniusOnline.Devices.Abstractions;

namespace GeniusOnline.Devices.Tests;

public class DeviceRegistryTests
{
    [Fact]
    public void Register_then_get_returns_adapter()
    {
        var registry = new DeviceRegistry();
        var adapter = new FakeAdapter("a", DeviceKind.Power);

        registry.Register(adapter);

        Assert.Same(adapter, registry.Get("a"));
        Assert.Single(registry.Adapters);
    }

    [Fact]
    public void Duplicate_registration_throws()
    {
        var registry = new DeviceRegistry();
        registry.Register(new FakeAdapter("a", DeviceKind.Power));
        Assert.Throws<InvalidOperationException>(() =>
            registry.Register(new FakeAdapter("a", DeviceKind.Power)));
    }

    [Fact]
    public void Status_changes_propagate_to_registry_subscribers()
    {
        var registry = new DeviceRegistry();
        var adapter = new FakeAdapter("a", DeviceKind.Power);
        registry.Register(adapter);

        DeviceStatusFrame? captured = null;
        registry.StatusChanged += (_, f) => captured = f;

        adapter.Bump(DeviceConnectionState.Connected);

        Assert.NotNull(captured);
        Assert.Equal("a", captured!.DeviceId);
        Assert.Equal(DeviceConnectionState.Connected, captured.Connection);
    }

    private sealed class FakeAdapter : DeviceAdapterBase
    {
        public FakeAdapter(string id, DeviceKind kind)
            : base(new DeviceIdentity(id, kind, $"Fake {id}", null, null)) { }

        public void Bump(DeviceConnectionState state) => State = state;

        public override Task ConnectAsync(CancellationToken ct = default) => Task.CompletedTask;
        public override Task DisconnectAsync(CancellationToken ct = default) => Task.CompletedTask;
        public override Task<DeviceCommandResult> ExecuteAsync(DeviceCommand command, CancellationToken ct = default)
            => Task.FromResult(new DeviceCommandResult(true));
    }
}
