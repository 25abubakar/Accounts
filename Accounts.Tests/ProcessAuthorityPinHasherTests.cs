using Accounts.Services.Services;

namespace Accounts.Tests;

public sealed class ProcessAuthorityPinHasherTests
{
    [Fact]
    public void Hash_UsesSalt_AndVerifiesOnlyTheCorrectPin()
    {
        var first = ProcessAuthorityPinHasher.Hash("012345");
        var second = ProcessAuthorityPinHasher.Hash("012345");

        Assert.NotEqual(first, second);
        Assert.True(ProcessAuthorityPinHasher.Verify("012345", first));
        Assert.False(ProcessAuthorityPinHasher.Verify("12345", first));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("plain-text-pin")]
    [InlineData("v1$999999999$bad$bad")]
    public void Verify_RejectsMissingOrMalformedHashes(string? encoded)
    {
        Assert.False(ProcessAuthorityPinHasher.Verify("1234", encoded));
    }
}
