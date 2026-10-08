using System.Security.Cryptography;
using Casino.Modules.Games.Infrastructure;

namespace Casino.Games.Tests;

public class SeedProtectorTests
{
    private const string Seed = "9f2c4a7e1b3d58606a1f0e9d8c7b6a5f4e3d2c1b0a99887766554433221100ff";

    private static SeedProtector NewProtector() => SeedProtector.FromBase64Key(SeedProtector.GenerateBase64Key());

    [Fact]
    public void Protected_value_can_be_opened_with_the_same_key_and_context()
    {
        var protector = NewProtector();

        var cipher = protector.Protect(Seed, "seed:user-1:pair-1");

        Assert.Equal(Seed, protector.Unprotect(cipher, "seed:user-1:pair-1"));
    }

    [Fact]
    public void Ciphertext_does_not_contain_the_plaintext_and_changes_every_time()
    {
        var protector = NewProtector();

        var first = protector.Protect(Seed, "ctx");
        var second = protector.Protect(Seed, "ctx");

        Assert.DoesNotContain(Seed, first, StringComparison.Ordinal);
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void A_ciphertext_cannot_be_moved_to_another_user_or_pair()
    {
        var protector = NewProtector();
        var cipher = protector.Protect(Seed, "seed:user-1:pair-1");

        Assert.ThrowsAny<CryptographicException>(() => protector.Unprotect(cipher, "seed:user-2:pair-1"));
        Assert.ThrowsAny<CryptographicException>(() => protector.Unprotect(cipher, "seed:user-1:pair-2"));
    }

    [Fact]
    public void A_different_master_key_cannot_open_it()
    {
        var cipher = NewProtector().Protect(Seed, "ctx");

        Assert.ThrowsAny<CryptographicException>(() => NewProtector().Unprotect(cipher, "ctx"));
    }

    [Fact]
    public void Tampering_with_the_ciphertext_is_detected()
    {
        var protector = NewProtector();
        var payload = Convert.FromBase64String(protector.Protect(Seed, "ctx"));
        payload[^1] ^= 0x01;

        Assert.ThrowsAny<CryptographicException>(() => protector.Unprotect(Convert.ToBase64String(payload), "ctx"));
    }

    [Theory]
    [InlineData("no-es-base64!!")]
    [InlineData("AAAA")]
    public void Malformed_values_are_rejected(string garbage)
    {
        Assert.ThrowsAny<CryptographicException>(() => NewProtector().Unprotect(garbage, "ctx"));
    }

    [Fact]
    public void Master_key_must_be_32_bytes_of_valid_base64()
    {
        Assert.Throws<ArgumentException>(() => new SeedProtector(new byte[16]));
        Assert.Throws<InvalidOperationException>(() => SeedProtector.FromBase64Key(null));
        Assert.Throws<InvalidOperationException>(() => SeedProtector.FromBase64Key("  "));
        Assert.Throws<InvalidOperationException>(() => SeedProtector.FromBase64Key("%%%"));
        Assert.Throws<ArgumentException>(() => SeedProtector.FromBase64Key(Convert.ToBase64String(new byte[10])));
    }

    [Fact]
    public void Generated_keys_are_valid_and_unique()
    {
        var first = SeedProtector.GenerateBase64Key();

        Assert.Equal(32, Convert.FromBase64String(first).Length);
        Assert.NotEqual(first, SeedProtector.GenerateBase64Key());
    }
}
