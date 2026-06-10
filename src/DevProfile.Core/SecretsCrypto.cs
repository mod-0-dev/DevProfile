using System.Security.Cryptography;
using System.Text;

namespace DevProfile.Core;

/// <summary>
/// Passphrase-based authenticated encryption for the optional secrets bundle.
/// PBKDF2(SHA-256) to derive a key, then AES-256-GCM. Pure managed, no native deps,
/// so a profile encrypted on one machine decrypts on any other.
///
/// On-disk layout (single file): MAGIC(4) | version(1) | salt(16) | nonce(12) | tag(16) | ciphertext
/// </summary>
public static class SecretsCrypto
{
    private static readonly byte[] Magic = "DPS1"u8.ToArray();
    private const byte Version = 1;
    private const int SaltLen = 16;
    private const int NonceLen = 12;   // AES-GCM standard
    private const int TagLen = 16;
    private const int KeyLen = 32;     // AES-256
    private const int Iterations = 210_000;

    public static byte[] Encrypt(byte[] plaintext, string passphrase)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltLen);
        var nonce = RandomNumberGenerator.GetBytes(NonceLen);
        var key = DeriveKey(passphrase, salt);

        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagLen];
        using (var gcm = new AesGcm(key, TagLen))
        {
            gcm.Encrypt(nonce, plaintext, ciphertext, tag);
        }
        CryptographicOperations.ZeroMemory(key);

        using var ms = new MemoryStream();
        ms.Write(Magic);
        ms.WriteByte(Version);
        ms.Write(salt);
        ms.Write(nonce);
        ms.Write(tag);
        ms.Write(ciphertext);
        return ms.ToArray();
    }

    public static byte[] Decrypt(byte[] blob, string passphrase)
    {
        if (blob.Length < Magic.Length + 1 + SaltLen + NonceLen + TagLen)
            throw new InvalidDataException("Secrets blob is truncated.");

        var span = blob.AsSpan();
        int o = 0;
        if (!span.Slice(0, Magic.Length).SequenceEqual(Magic))
            throw new InvalidDataException("Not a DevProfile secrets file.");
        o += Magic.Length;
        if (span[o++] != Version) throw new InvalidDataException("Unsupported secrets version.");

        var salt = span.Slice(o, SaltLen).ToArray(); o += SaltLen;
        var nonce = span.Slice(o, NonceLen).ToArray(); o += NonceLen;
        var tag = span.Slice(o, TagLen).ToArray(); o += TagLen;
        var ciphertext = span.Slice(o).ToArray();

        var key = DeriveKey(passphrase, salt);
        var plaintext = new byte[ciphertext.Length];
        try
        {
            using var gcm = new AesGcm(key, TagLen);
            gcm.Decrypt(nonce, ciphertext, tag, plaintext);
        }
        catch (CryptographicException)
        {
            throw new CryptographicException("Wrong passphrase or corrupted secrets bundle.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
        return plaintext;
    }

    private static byte[] DeriveKey(string passphrase, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(passphrase), salt, Iterations, HashAlgorithmName.SHA256, KeyLen);
}
