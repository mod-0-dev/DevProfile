using System.Security.Cryptography;
using System.Text;
using DevProfile.Core;
using Xunit;

namespace DevProfile.Core.Tests;

public class SecretsCryptoTests
{
    private static readonly byte[] Sample = Encoding.UTF8.GetBytes("ssh-ed25519 AAAA... very secret");

    [Fact]
    public void RoundTrip_ReturnsOriginalPlaintext()
    {
        var blob = SecretsCrypto.Encrypt(Sample, "correct horse battery staple");
        var back = SecretsCrypto.Decrypt(blob, "correct horse battery staple");
        Assert.Equal(Sample, back);
    }

    [Fact]
    public void RoundTrip_EmptyPlaintext_Works()
    {
        var blob = SecretsCrypto.Encrypt(Array.Empty<byte>(), "pw");
        Assert.Empty(SecretsCrypto.Decrypt(blob, "pw"));
    }

    [Fact]
    public void WrongPassphrase_Throws()
    {
        var blob = SecretsCrypto.Encrypt(Sample, "right");
        Assert.Throws<CryptographicException>(() => SecretsCrypto.Decrypt(blob, "wrong"));
    }

    [Fact]
    public void TamperedCiphertext_Throws()
    {
        var blob = SecretsCrypto.Encrypt(Sample, "pw");
        blob[^1] ^= 0xFF; // flip a bit in the ciphertext
        Assert.Throws<CryptographicException>(() => SecretsCrypto.Decrypt(blob, "pw"));
    }

    [Fact]
    public void TruncatedBlob_Throws()
    {
        var blob = SecretsCrypto.Encrypt(Sample, "pw");
        Assert.Throws<InvalidDataException>(() => SecretsCrypto.Decrypt(blob[..10], "pw"));
    }

    [Fact]
    public void WrongMagic_Throws()
    {
        var blob = SecretsCrypto.Encrypt(Sample, "pw");
        blob[0] = (byte)'X';
        Assert.Throws<InvalidDataException>(() => SecretsCrypto.Decrypt(blob, "pw"));
    }

    [Fact]
    public void Encrypt_IsSalted_SameInputDiffersAcrossCalls()
    {
        var a = SecretsCrypto.Encrypt(Sample, "pw");
        var b = SecretsCrypto.Encrypt(Sample, "pw");
        Assert.NotEqual(a, b);
    }
}
