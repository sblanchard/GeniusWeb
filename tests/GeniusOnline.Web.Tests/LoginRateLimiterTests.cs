using GeniusOnline.Web.Auth;

namespace GeniusOnline.Web.Tests;

public class LoginRateLimiterTests
{
    [Fact]
    public void Allows_first_five_attempts_then_blocks()
    {
        var limiter = new LoginRateLimiter();

        for (int i = 0; i < 5; i++)
            Assert.True(limiter.TryRegister("10.0.0.1"));

        Assert.False(limiter.TryRegister("10.0.0.1"));
    }

    [Fact]
    public void Reset_clears_counter()
    {
        var limiter = new LoginRateLimiter();
        for (int i = 0; i < 5; i++) limiter.TryRegister("10.0.0.2");
        limiter.Reset("10.0.0.2");
        Assert.True(limiter.TryRegister("10.0.0.2"));
    }

    [Fact]
    public void Buckets_are_per_ip()
    {
        var limiter = new LoginRateLimiter();
        for (int i = 0; i < 5; i++) limiter.TryRegister("10.0.0.3");
        Assert.True(limiter.TryRegister("10.0.0.4"));
    }
}
