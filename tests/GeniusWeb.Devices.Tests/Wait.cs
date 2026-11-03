namespace GeniusWeb.Devices.Tests;

/// <summary>Waiting on something another thread is doing, without sleeping a fixed time.</summary>
internal static class Wait
{
    public static async Task Until(Func<bool> condition, string what, TimeSpan limit)
    {
        var deadline = DateTime.UtcNow + limit;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new Xunit.Sdk.XunitException($"Not true within {limit.TotalSeconds:0} s: {what}");
            await Task.Delay(50);
        }
    }
}
