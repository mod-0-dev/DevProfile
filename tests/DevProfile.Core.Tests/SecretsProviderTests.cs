using System.IO.Compression;
using DevProfile.Core;
using DevProfile.Core.Providers;
using Xunit;

namespace DevProfile.Core.Tests;

public sealed class SecretsProviderTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("devprofile-tests-").FullName;
    private string RestoreRoot => Path.Combine(_dir, "home");
    private string ProfileDir => Path.Combine(_dir, "profile");

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private void WriteEncryptedBundle(string passphrase, params (string EntryName, string Content)[] entries)
    {
        using var zipStream = new MemoryStream();
        using (var zip = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                var entry = zip.CreateEntry(name);
                using var w = new StreamWriter(entry.Open());
                w.Write(content);
            }
        }
        var dest = Path.Combine(ProfileDir, "secrets", "secrets.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        File.WriteAllBytes(dest, SecretsCrypto.Encrypt(zipStream.ToArray(), passphrase));
    }

    private static readonly PlanItem Item = new("secrets", "secrets", "encrypted", PlanAction.Install);

    [Fact]
    public async Task Apply_RestoresEntriesUnderRoot()
    {
        Directory.CreateDirectory(RestoreRoot);
        WriteEncryptedBundle("pw", (".npmrc", "//registry/:_authToken=abc"));

        await new SecretsProvider(RestoreRoot).ApplyAsync(
            ProfileDir, Item, new ApplyOptions(Passphrase: "pw"), _ => { });

        Assert.Equal("//registry/:_authToken=abc", File.ReadAllText(Path.Combine(RestoreRoot, ".npmrc")));
    }

    [Fact]
    public async Task Apply_RefusesZipSlipEntries()
    {
        Directory.CreateDirectory(RestoreRoot);
        WriteEncryptedBundle("pw", ("../evil.txt", "pwned"), (@"..\evil2.txt", "pwned"));
        var logged = new List<string>();

        await new SecretsProvider(RestoreRoot).ApplyAsync(
            ProfileDir, Item, new ApplyOptions(Passphrase: "pw"), logged.Add);

        Assert.False(File.Exists(Path.Combine(_dir, "evil.txt")));
        Assert.False(File.Exists(Path.Combine(_dir, "evil2.txt")));
        Assert.Equal(2, logged.Count(l => l.Contains("refusing")));
    }

    [Fact]
    public async Task Apply_BacksUpExistingFile()
    {
        Directory.CreateDirectory(RestoreRoot);
        File.WriteAllText(Path.Combine(RestoreRoot, ".npmrc"), "old");
        WriteEncryptedBundle("pw", (".npmrc", "new"));

        await new SecretsProvider(RestoreRoot).ApplyAsync(
            ProfileDir, Item, new ApplyOptions(Passphrase: "pw"), _ => { });

        Assert.Equal("new", File.ReadAllText(Path.Combine(RestoreRoot, ".npmrc")));
        Assert.Equal("old", File.ReadAllText(Path.Combine(RestoreRoot, ".npmrc.devprofile.bak")));
    }

    [Fact]
    public async Task Apply_NoPassphrase_SkipsWithMessage()
    {
        Directory.CreateDirectory(RestoreRoot);
        WriteEncryptedBundle("pw", (".npmrc", "token"));
        var logged = new List<string>();

        await new SecretsProvider(RestoreRoot).ApplyAsync(
            ProfileDir, Item, new ApplyOptions(Passphrase: null), logged.Add);

        Assert.False(File.Exists(Path.Combine(RestoreRoot, ".npmrc")));
        Assert.Contains(logged, l => l.Contains("no passphrase"));
    }
}
