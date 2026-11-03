using RotatorGenius.Communication;

namespace GeniusWeb.Devices.Tests;

/// <summary>
/// What the library does when the controller's TCP link dies. On the Pi, on 2026-10-30, the
/// link broke and the library polled the dead socket ten times a second for 37 hours
/// (1.3 million "connection error" warnings, every command "Broken pipe") and never
/// reconnected, so the panel kept the last status it had read: a heading three days old.
/// Fixed upstream in GeniusDesktop by 687c40b (a failed poll drops the link) and 7653270
/// (the automatic reconnect no longer cancels itself before it dials).
/// </summary>
public class RotatorLinkRecoveryTests
{
    private static readonly TimeSpan Reconnect = TimeSpan.FromSeconds(15);

    [Fact]
    public async Task A_link_that_dies_is_followed_again_and_the_headings_are_fresh()
    {
        using var controller = new FakeRotatorController(azimuth: 100);
        var device = new RotatorDevice { Address = "127.0.0.1", Port = controller.Port };
        var heading = Watch(device);
        try
        {
            await device.ConnectAsync();
            await Wait.Until(() => heading.Value == 100, "the first status is read", TimeSpan.FromSeconds(5));
            var connectionsBefore = controller.Accepted;

            // The cable is pulled and, while it is out, the rotator is turned. The order
            // matters: a reply already on its way down the old link must not be able to
            // carry the new heading and satisfy this test without any reconnect.
            controller.DropConnections();
            controller.Azimuth = 250;

            await Wait.Until(() => heading.Value == 250, "the rotator is followed again and its new heading read", Reconnect);
            Assert.True(controller.Accepted > connectionsBefore, "the library dialled the controller again");
        }
        finally
        {
            device.Disconnect();
        }
    }

    [Fact]
    public async Task A_link_that_dies_is_reported_once_not_ten_times_a_second()
    {
        using var controller = new FakeRotatorController(azimuth: 100);
        var device = new RotatorDevice { Address = "127.0.0.1", Port = controller.Port };
        var heading = Watch(device);
        var errors = 0;
        device.ConnectionError += (_, _) => Interlocked.Increment(ref errors);
        try
        {
            await device.ConnectAsync();
            await Wait.Until(() => heading.Value == 100, "the first status is read", TimeSpan.FromSeconds(5));
            var before = Volatile.Read(ref errors);

            controller.DropConnections();
            await Task.Delay(TimeSpan.FromSeconds(2));

            // The poll interval is 100 ms: a loop that carries on against a dead socket
            // reports about twenty errors in these two seconds. At least one must be
            // reported, or a link that dies silently would pass too.
            var reported = Volatile.Read(ref errors) - before;
            Assert.InRange(reported, 1, 4);
        }
        finally
        {
            device.Disconnect();
        }
    }

    /// <summary>
    /// The first rotator's heading as of the last status the library handed over. Read on
    /// the poll thread, which is also the thread that fills the rotator list, so there is
    /// no looking at a list while it grows.
    /// </summary>
    private static Heading Watch(RotatorDevice device)
    {
        var heading = new Heading();
        device.DataReceived += (_, _) =>
            heading.Value = device.Rotators.Count > 0 ? device.Rotators[0].Azimuth : -1;
        return heading;
    }

    private sealed class Heading
    {
        private int _value = -1;

        public int Value
        {
            get => Volatile.Read(ref _value);
            set => Volatile.Write(ref _value, value);
        }
    }
}
