using System.Collections;

namespace GeniusWeb.Web.Services;

/// <summary>One rotator on a controller, as the panel shows it.</summary>
/// <param name="Index">The controller's slot, counting from 0: what a command addresses.</param>
/// <param name="Name">What the controller calls it, or "Rotator 2" where it says nothing.</param>
/// <param name="Azimuth">The heading, or null where nothing is reporting one.</param>
/// <param name="Target">The heading it is turning to, or null.</param>
public sealed record RotatorReading(int Index, string Name, int? Azimuth, int? Target)
{
    /// <summary>Whether there is a heading to draw, and so a rotator worth commanding.</summary>
    public bool HasHeading => Azimuth is not null;
}

/// <summary>Reads the rotators out of a Rotator Genius status frame.</summary>
public static class RotatorReadings
{
    /// <summary>The controller's value for "nothing here".</summary>
    private const int NoValue = 999;

    public static IReadOnlyList<RotatorReading> From(object? rotators)
    {
        if (rotators is not IEnumerable slots || rotators is string) return [];

        var read = new List<RotatorReading>();
        foreach (var slot in slots)
        {
            if (Value<int?>(slot, "Number") is not { } index) continue;
            var name = Value<string>(slot, "Name")?.Trim();
            read.Add(new RotatorReading(
                index,
                string.IsNullOrEmpty(name) ? $"Rotator {index + 1}" : name,
                Heading(Value<int?>(slot, "Azimuth")),
                Heading(Value<int?>(slot, "Target"))));
        }
        return read.OrderBy(r => r.Index).ToList();
    }

    private static int? Heading(int? value) => value == NoValue ? null : value;

    private static T? Value<T>(object? slot, string property) =>
        slot?.GetType().GetProperty(property)?.GetValue(slot) is T value ? value : default;
}
