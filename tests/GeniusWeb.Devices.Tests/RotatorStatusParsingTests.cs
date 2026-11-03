using System.Reflection;
using System.Runtime.ExceptionServices;
using RotatorGenius.Communication;
using static GeniusWeb.Devices.Tests.RotatorStatusFrames;

namespace GeniusWeb.Devices.Tests;

/// <summary>
/// Parsing the Rotator Genius "|h" status reply, which drives every heading on the panel.
/// Ported from GeniusDesktop's StatusResponseTests (fix 5ad113f): the vendored library had
/// no tests of its own, and its parser threw on a blank numeric field.
/// </summary>
public class RotatorStatusParsingTests
{
    /// <summary>
    /// The library does not make its parser public; it is exercised here exactly as the poll
    /// loop does, by handing it the reply. An exception comes out as itself, not wrapped.
    /// </summary>
    private static void Process(RotatorDevice device, string data)
    {
        var parse = typeof(RotatorDevice).GetMethod(
            "ProcessStatusResponse", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("RotatorDevice.ProcessStatusResponse not found");
        try
        {
            parse.Invoke(device, [data]);
        }
        catch (TargetInvocationException wrapped) when (wrapped.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(wrapped.InnerException).Throw();
        }
    }

    [Fact]
    public void A_complete_status_lands_on_the_rotator()
    {
        var device = new RotatorDevice();

        Process(device, Status(1, Block()));

        var rotator = Assert.Single(device.Rotators);
        Assert.Equal(180, rotator.Azimuth);
        Assert.Equal(360, rotator.LimitCW);
        Assert.Equal(0, rotator.LimitCCW);
        Assert.Equal("A", rotator.Mode);
        Assert.Equal(180, rotator.Target);
        Assert.Equal("Main", rotator.Name);
    }

    [Fact]
    public void Both_rotators_of_a_controller_are_read()
    {
        var device = new RotatorDevice();

        Process(device, Status(2, Block(azimuth: "347", name: "RG1         "), Block(azimuth: " 50", name: "RG2         ")));

        Assert.Equal(2, device.Rotators.Count);
        Assert.Equal(347, device.Rotators[0].Azimuth);
        Assert.Equal(50, device.Rotators[1].Azimuth);
    }

    [Theory]
    [InlineData("start")]
    [InlineData("target")]
    public void A_blank_field_in_the_second_rotator_does_not_freeze_its_heading(string blankField)
    {
        // The reported fault: RG2 read 347 once, then turned to 50. A later status left one
        // of its other fields blank, int.Parse threw before the azimuth was stored, and
        // the panel stayed on 347 while RG2 sat at 50.
        var device = new RotatorDevice();
        Process(device, Status(2, Block(azimuth: "120"), Block(azimuth: "347")));

        var blank = "   ";
        var second = blankField switch
        {
            "start" => Block(azimuth: " 50", start: blank),
            "target" => Block(azimuth: " 50", target: blank),
            _ => throw new ArgumentOutOfRangeException(nameof(blankField), blankField, "no such case"),
        };
        Process(device, Status(2, Block(azimuth: "120"), second));

        Assert.Equal(120, device.Rotators[0].Azimuth);
        Assert.Equal(50, device.Rotators[1].Azimuth);
    }

    [Fact]
    public void A_blank_field_does_not_look_like_a_lost_connection()
    {
        // A field the device left blank is not an error. Read with int.Parse it threw, and the
        // poll loop took that for a lost connection: it reported "Connection lost" and
        // reconnected into a status of exactly the same shape, while the status event that
        // follows the parse was never raised. The socket was never the problem.
        var device = new RotatorDevice();

        var thrown = Record.Exception(() => Process(device, Status(1, Block(start: "   ", target: "   "))));

        Assert.Null(thrown);
        Assert.Equal(180, Assert.Single(device.Rotators).Azimuth);
    }

    [Theory]
    [InlineData("   ")] // the heading is blank
    [InlineData("1 2")] // padding lands inside the number
    public void A_heading_the_device_did_not_fill_in_stays_unknown(string azimuth)
    {
        var device = new RotatorDevice();

        Process(device, Status(1, Block(azimuth: azimuth)));

        var rotator = Assert.Single(device.Rotators);
        Assert.Equal(999, rotator.Azimuth);     // the model's own "unknown", kept
        Assert.Equal(360, rotator.LimitCW);     // and the rest of the block still read
        Assert.Equal("Main", rotator.Name);
    }

    [Fact]
    public void A_blank_offset_leaves_the_offset_alone()
    {
        var device = new RotatorDevice();

        Process(device, Status(1, Block(offset: "    ")));

        var rotator = Assert.Single(device.Rotators);
        Assert.Equal(0, rotator.Offset);
        Assert.Equal(180, rotator.Azimuth);
    }

    [Theory]
    [InlineData("")]
    [InlineData("|xnonsense")]
    [InlineData("|h1.short")]
    public void A_reply_that_is_not_a_status_is_ignored(string data)
    {
        var device = new RotatorDevice();

        Process(device, data);

        Assert.Empty(device.Rotators);
    }

    [Fact]
    public void A_blank_field_keeps_the_last_reading_not_the_models_default()
    {
        // If the parser ever builds a fresh Rotator per status, a blank field would fall back
        // to the model's own default and every test above would still pass. Offset is the one
        // signed field.
        var device = new RotatorDevice();
        Process(device, Status(1, Block(azimuth: "347", offset: "  -5", target: "123", start: " 77")));

        Process(device, Status(1, Block(azimuth: "   ", offset: "    ", target: "   ", start: "   ")));

        var rotator = Assert.Single(device.Rotators);
        Assert.Equal(347, rotator.Azimuth);
        Assert.Equal(-5, rotator.Offset);
        Assert.Equal(123, rotator.Target);
        Assert.Equal(77, rotator.Start);
    }
}
