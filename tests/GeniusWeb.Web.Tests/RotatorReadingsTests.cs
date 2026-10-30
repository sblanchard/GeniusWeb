using GeniusWeb.Web.Services;

namespace GeniusWeb.Web.Tests;

public class RotatorReadingsTests
{
    private static object Slot(int number, string name, int azimuth, int target = 999) =>
        new { Number = number, Name = name, Azimuth = azimuth, Target = target };

    [Fact]
    public void Both_rotators_of_a_controller_are_read()
    {
        var read = RotatorReadings.From(new[] { Slot(0, "Tower", 76), Slot(1, "Beam", 342, 90) });

        Assert.Equal(2, read.Count);
        Assert.Equal(new RotatorReading(0, "Tower", 76, null), read[0]);
        Assert.Equal(new RotatorReading(1, "Beam", 342, 90), read[1]);
    }

    [Fact]
    public void They_come_in_the_controllers_order_whatever_order_they_arrive_in()
    {
        var read = RotatorReadings.From(new[] { Slot(1, "Beam", 342), Slot(0, "Tower", 76) });

        Assert.Equal(new[] { 0, 1 }, read.Select(r => r.Index));
    }

    [Fact]
    public void A_heading_of_999_is_no_heading_at_all()
    {
        // The controller's value for a slot with nothing reporting: not a bearing to draw or turn from.
        var read = RotatorReadings.From(new[] { Slot(1, "Rotator 2", 999) });

        Assert.Null(read[0].Azimuth);
        Assert.False(read[0].HasHeading);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_rotator_with_no_name_is_called_by_its_number_counting_from_one(string name) =>
        Assert.Equal("Rotator 2", RotatorReadings.From(new[] { Slot(1, name, 10) })[0].Name);

    [Fact]
    public void A_name_is_trimmed_of_the_padding_the_controller_sends() =>
        Assert.Equal("Tower", RotatorReadings.From(new[] { Slot(0, "Tower   ", 10) })[0].Name);

    [Theory]
    [InlineData(null)]
    [InlineData("not a list")]
    [InlineData(42)]
    public void A_status_with_no_rotators_in_it_reads_as_none(object? raw) =>
        Assert.Empty(RotatorReadings.From(raw));
}
