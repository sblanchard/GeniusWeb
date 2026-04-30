using GeniusOnline.Contracts;
using GeniusOnline.Devices.Antenna;
using GeniusOnline.Devices.Power;
using GeniusOnline.Devices.Tuner;
using Microsoft.Extensions.Logging.Abstractions;

namespace GeniusOnline.Devices.Tests;

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
}
