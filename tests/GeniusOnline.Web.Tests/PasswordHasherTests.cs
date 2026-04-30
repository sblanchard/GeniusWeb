using GeniusOnline.Web.Auth;

namespace GeniusOnline.Web.Tests;

public class PasswordHasherTests
{
    [Fact]
    public void Hash_then_verify_round_trips()
    {
        var encoded = PasswordHasher.Hash("correct horse battery staple");
        Assert.True(PasswordHasher.Verify("correct horse battery staple", encoded));
        Assert.False(PasswordHasher.Verify("wrong password", encoded));
    }

    [Fact]
    public void Verify_returns_false_for_malformed_input()
    {
        Assert.False(PasswordHasher.Verify("any", ""));
        Assert.False(PasswordHasher.Verify("any", "not-a-hash"));
        Assert.False(PasswordHasher.Verify("", "pbkdf2.sha256.1.aaa.bbb"));
    }

    [Fact]
    public void Two_hashes_of_same_password_differ_by_salt()
    {
        var a = PasswordHasher.Hash("same");
        var b = PasswordHasher.Hash("same");
        Assert.NotEqual(a, b);
        Assert.True(PasswordHasher.Verify("same", a));
        Assert.True(PasswordHasher.Verify("same", b));
    }
}
