using System.Collections.Concurrent;
using GeniusWeb.Contracts;
using GeniusWeb.Devices.Rotator;
using Microsoft.Extensions.Logging.Abstractions;

namespace GeniusWeb.Devices.Tests;

/// <summary>
/// The adapter's side of a link that dies and comes back. The library follows the
/// controller again by itself; the adapter must then say so, or the panel shows a
/// device that is "Faulted" while fresh headings flow.
/// </summary>
public class RotatorAdapterRecoveryTests
{
    private static readonly TimeSpan Reconnect = TimeSpan.FromSeconds(15);

    [Fact]
    public async Task An_adapter_whose_link_died_and_came_back_is_connected_again_with_fresh_headings()
    {
        using var controller = new FakeRotatorController(azimuth: 100);
        await using var adapter = AdapterFor(controller);
        var frames = new ConcurrentQueue<(DeviceConnectionState Connection, int? Azimuth)>();
        adapter.StatusChanged += (_, frame) => frames.Enqueue((frame.Connection, FirstAzimuth(frame)));
        try
        {
            await adapter.ConnectAsync();
            await Wait.Until(() => Reads(adapter.LastStatus, 100), "the first status is on the panel", TimeSpan.FromSeconds(5));

            // The link is cut first and the rotator turned after, so no reply already on
            // its way can carry the new heading without a reconnect.
            controller.DropConnections();
            controller.Azimuth = 250;

            await Wait.Until(
                () => Reads(adapter.LastStatus, 250),
                "the panel shows the new heading and says the device is connected",
                Reconnect);

            var seen = frames.ToArray();
            var lastFaulted = Array.FindLastIndex(seen, frame => frame.Connection == DeviceConnectionState.Faulted);
            Assert.True(lastFaulted >= 0, "the adapter said the device was Faulted while the link was down");

            // The first frame that says "connected" again must carry the new heading, not
            // the one from before the drop with only the state changed.
            var firstConnectedAgain = seen.Skip(lastFaulted + 1).First(frame => frame.Connection == DeviceConnectionState.Connected);
            Assert.Equal(250, firstConnectedAgain.Azimuth);
        }
        finally
        {
            await adapter.DisconnectAsync();
        }
    }

    [Fact]
    public async Task A_deliberately_disconnected_adapter_stays_disconnected()
    {
        // The adapter now marks itself Connected whenever a status arrives. A manual
        // disconnect must still win over a status that is on its way when it happens.
        using var controller = new FakeRotatorController(azimuth: 100);
        await using var adapter = AdapterFor(controller);
        await adapter.ConnectAsync();
        await Wait.Until(() => Reads(adapter.LastStatus, 100), "the first status is on the panel", TimeSpan.FromSeconds(5));

        await adapter.DisconnectAsync();
        await Task.Delay(TimeSpan.FromSeconds(1));

        Assert.Equal(DeviceConnectionState.Disconnected, adapter.State);
        Assert.Equal(DeviceConnectionState.Disconnected, adapter.LastStatus!.Connection);
    }

    private static RotatorAdapter AdapterFor(FakeRotatorController controller) =>
        new(new DeviceIdentity("rot-1", DeviceKind.Rotator, "Rotator Genius", "127.0.0.1", controller.Port),
            NullLogger<RotatorAdapter>.Instance);

    /// <summary>Whether the frame says "connected" and carries this heading for the first rotator.</summary>
    private static bool Reads(DeviceStatusFrame? frame, int azimuth) =>
        frame is { Connection: DeviceConnectionState.Connected } && FirstAzimuth(frame) == azimuth;

    private static int? FirstAzimuth(DeviceStatusFrame frame)
    {
        if (!frame.Payload.TryGetValue("rotators", out var raw) || raw is not System.Collections.IEnumerable rotators)
            return null;
        foreach (var rotator in rotators)
            return rotator.GetType().GetProperty("Azimuth")?.GetValue(rotator) as int?;
        return null;
    }
}
