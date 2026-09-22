namespace ControlTime.Tests;

public sealed class PasswordHasherTests
{
    private readonly Pbkdf2PasswordHasher _hasher = new();

    [Fact]
    public void Hash_ReturnsNonEmptyValueDifferentFromPassword()
    {
        var hash = _hasher.Hash("correct horse battery staple");

        Assert.False(string.IsNullOrWhiteSpace(hash));
        Assert.DoesNotContain("correct horse battery staple", hash);
    }

    [Fact]
    public void Hash_SamePasswordTwice_ProducesDifferentHashes()
    {
        var hash1 = _hasher.Hash("admin-password");
        var hash2 = _hasher.Hash("admin-password");

        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void Verify_CorrectPassword_ReturnsTrue()
    {
        var hash = _hasher.Hash("admin-password");

        Assert.True(_hasher.Verify("admin-password", hash));
    }

    [Fact]
    public void Verify_WrongPassword_ReturnsFalse()
    {
        var hash = _hasher.Hash("admin-password");

        Assert.False(_hasher.Verify("wrong-password", hash));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Hash_EmptyOrWhitespacePassword_Throws(string password)
    {
        Assert.Throws<ArgumentException>(() => _hasher.Hash(password));
    }

    [Fact]
    public void Verify_MalformedStoredHash_ReturnsFalse()
    {
        Assert.False(_hasher.Verify("admin-password", "not-a-real-hash"));
    }

    [Fact]
    public void IsValidFormat_FreshHash_ReturnsTrue()
    {
        Assert.True(_hasher.IsValidFormat(_hasher.Hash("admin-password")));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-real-hash")]
    [InlineData("PBKDF2.SHA256.nope.AAAA.AAAA")]
    [InlineData("PBKDF2.SHA512.100000.QUJD.QUJD")]
    [InlineData("PBKDF2.SHA256.100000.notbase64!.QUJD")]
    [InlineData("PBKDF2.SHA256.0.QUJD.QUJD")]
    public void IsValidFormat_CorruptedValues_ReturnsFalse(string? stored)
    {
        Assert.False(_hasher.IsValidFormat(stored));
    }

    [Fact]
    public void IsValidFormat_EmptySaltOrKey_ReturnsFalse()
    {
        Assert.False(_hasher.IsValidFormat("PBKDF2.SHA256.100000..QUJD"));
        Assert.False(_hasher.IsValidFormat("PBKDF2.SHA256.100000.QUJD."));
    }
}
