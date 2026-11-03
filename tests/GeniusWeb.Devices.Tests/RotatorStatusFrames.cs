namespace GeniusWeb.Devices.Tests;

/// <summary>The Rotator Genius "|h" status reply, built the way the controller pads it.</summary>
internal static class RotatorStatusFrames
{
    /// <summary>
    /// One 34-character rotator block. The protocol pads its numeric fields with spaces,
    /// which is why the pattern that reads them accepts whitespace.
    /// </summary>
    public static string Block(
        string azimuth = "180",
        string limitCw = "360",
        string limitCcw = "  0",
        string mode = "A",
        string rotation = "0",
        string offset = "   0",
        string target = "180",
        string start = "  0",
        string limited = "\0",
        string name = "Main        ")
        => azimuth + limitCw + limitCcw + mode + rotation + offset + target + start + limited + name;

    /// <summary>A whole reply: "|h", the rotator count, a separator, then two 34-character blocks.</summary>
    public static string Status(int count, string first, string? second = null)
        => $"|h{count}." + first + (second ?? new string(' ', 34));
}
