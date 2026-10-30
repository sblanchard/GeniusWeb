using GeniusWeb.Contracts;
using GeniusWeb.Devices.Antenna;
using GeniusWeb.Devices.Power;
using GeniusWeb.Devices.Rotator;
using GeniusWeb.Devices.Tuner;
using Microsoft.Extensions.Logging.Abstractions;

namespace GeniusWeb.Devices.Tests;

public class AdapterContractTests
{
    [Fact]
    public async Task Antenna_rejects_unknown_verb()
    {
        var a = new AntennaAdapter(
            new DeviceIdentity("ant", DeviceKind.Antenna, "Ant", "192.0.2.99", 9007),
            NullLogger<AntennaAdapter>.Instance);

        var r = await a.ExecuteAsync(new DeviceCommand("ant", "fly"));
        Assert.False(r.Success);
        Assert.Contains("Unknown verb", r.Message);
    }

    [Fact]
    public async Task Power_rejects_unknown_verb_when_disconnected()
    {
        var p = new PowerAdapter(
            new DeviceIdentity("p", DeviceKind.Power, "Pwr", "192.0.2.99", 9007),
            NullLogger<PowerAdapter>.Instance);

        var r = await p.ExecuteAsync(new DeviceCommand("p", "doSomething"));
        Assert.False(r.Success);
    }

    [Fact]
    public async Task Tuner_rejects_unknown_verb_when_disconnected()
    {
        var t = new TunerAdapter(
            new DeviceIdentity("t", DeviceKind.Tuner, "Tun", "192.0.2.99", 9010),
            NullLogger<TunerAdapter>.Instance);

        var r = await t.ExecuteAsync(new DeviceCommand("t", "fly"));
        Assert.False(r.Success);
    }

    [Theory]
    [InlineData(-1, 2, false)]
    [InlineData(0, 2, true)]
    [InlineData(1, 2, true)]
    [InlineData(2, 2, false)]
    [InlineData(1, 1, false)]
    [InlineData(1, 0, true)] // no status yet: the controller is the judge of what it has
    [InlineData(-1, 0, false)]
    public void Rotator_addresses_only_what_the_controller_has(int index, int known, bool allowed) =>
        Assert.Equal(allowed, RotatorAdapter.IsKnownRotator(index, known));

    [Fact]
    public async Task Rotator_refuses_to_turn_a_rotator_that_does_not_exist()
    {
        var r = new RotatorAdapter(
            new DeviceIdentity("r", DeviceKind.Rotator, "Rot", "192.0.2.99", 9006),
            NullLogger<RotatorAdapter>.Instance);

        var result = await r.ExecuteAsync(new DeviceCommand("r", "rotateTo",
            new Dictionary<string, object?> { ["rotator"] = -1, ["azimuth"] = 90 }));

        Assert.False(result.Success);
        Assert.Contains("No such rotator", result.Message);
    }

    [Fact]
    public async Task Rotator_stop_is_never_refused_for_the_rotator_it_names()
    {
        // STOP stops the whole controller, and is the one command that must not be argued with.
        var r = new RotatorAdapter(
            new DeviceIdentity("r", DeviceKind.Rotator, "Rot", "192.0.2.99", 9006),
            NullLogger<RotatorAdapter>.Instance);

        var result = await r.ExecuteAsync(new DeviceCommand("r", "stop",
            new Dictionary<string, object?> { ["rotator"] = -1 }));

        Assert.DoesNotContain("No such rotator", result.Message ?? "");
    }
}
