using System.IO.Compression;

namespace DevProfile.Core.Providers;

/// <summary>
/// Opt-in, passphrase-encrypted bundle of sensitive files (SSH keys, .npmrc tokens).
/// The files are zipped in memory, then AES-256-GCM encrypted via <see cref="SecretsCrypto"/>;
/// nothing sensitive is ever written to the profile in cleartext.
/// </summary>
public sealed class SecretsProvider : IProvider
{
    private readonly string _restoreRoot;

    public SecretsProvider() : this(KnownPaths.UserProfile) { }

    /// <summary>Test seam: restore somewhere other than the real %USERPROFILE%.</summary>
    internal SecretsProvider(string restoreRoot) => _restoreRoot = restoreRoot;

    public string Id => "secrets";
    public string DisplayName => "SSH keys & tokens (encrypted)";
    public ProviderCategory Category => ProviderCategory.Secrets;
    public bool ContainsSecrets => true;

    private string StorePath(string profileDir) => Path.Combine(profileDir, Id, "secrets.bin");

    public Task<DiscoveryResult> DiscoverAsync(CancellationToken ct = default)
    {
        var sources = EnumerateSources().ToList();
        if (sources.Count == 0)
            return Task.FromResult(DiscoveryResult.Missing("no .ssh / .npmrc found"));
        return Task.FromResult(DiscoveryResult.Found($"{sources.Count} file(s) — needs passphrase"));
    }

    public async Task CaptureAsync(string profileDir, ExportOptions options, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(options.Passphrase))
            throw new InvalidOperationException("A passphrase is required to capture secrets.");

        var sources = EnumerateSources().ToList();
        if (sources.Count == 0) return;

        using var zipStream = new MemoryStream();
        using (var zip = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (entryName, fullPath) in sources)
            {
                var entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
                await using var es = entry.Open();
                await using var fs = File.OpenRead(fullPath);
                await fs.CopyToAsync(es, ct).ConfigureAwait(false);
            }
        }

        var encrypted = SecretsCrypto.Encrypt(zipStream.ToArray(), options.Passphrase);
        var dest = StorePath(profileDir);
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        await File.WriteAllBytesAsync(dest, encrypted, ct).ConfigureAwait(false);
    }

    public Task<IReadOnlyList<PlanItem>> PlanAsync(string profileDir, CancellationToken ct = default)
    {
        var stored = StorePath(profileDir);
        IReadOnlyList<PlanItem> result = File.Exists(stored)
            ? new[] { new PlanItem(Id, DisplayName, "encrypted", PlanAction.Install, "decrypts on apply — passphrase required") }
            : Array.Empty<PlanItem>();
        return Task.FromResult(result);
    }

    public async Task ApplyAsync(string profileDir, PlanItem item, ApplyOptions options, Action<string> log, CancellationToken ct = default)
    {
        if (item.Action == PlanAction.Skip) return;
        var stored = StorePath(profileDir);
        if (!File.Exists(stored)) return;
        if (string.IsNullOrEmpty(options.Passphrase))
        {
            log("  ! secrets present but no passphrase supplied — skipped.");
            return;
        }

        var plain = SecretsCrypto.Decrypt(await File.ReadAllBytesAsync(stored, ct).ConfigureAwait(false), options.Passphrase);
        var root = Path.GetFullPath(_restoreRoot + Path.DirectorySeparatorChar);
        using var zipStream = new MemoryStream(plain);
        using var zip = new ZipArchive(zipStream, ZipArchiveMode.Read);
        foreach (var entry in zip.Entries)
        {
            // Zip-slip guard: a tampered bundle must not write outside %USERPROFILE%
            // via entry names like "..\..\evil".
            var target = Path.GetFullPath(Path.Combine(root, entry.FullName));
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                log($"  ! refusing entry outside the user profile: {entry.FullName}");
                continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (File.Exists(target))
            {
                File.Copy(target, target + ".devprofile.bak", overwrite: true);
            }
            await using (var es = entry.Open())
            await using (var fs = File.Create(target))
            {
                await es.CopyToAsync(fs, ct).ConfigureAwait(false);
            }
            log($"  restored {entry.FullName}");

            if (target.StartsWith(Path.GetFullPath(KnownPaths.SshDir), StringComparison.OrdinalIgnoreCase))
                await TightenAclAsync(target, log, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Windows OpenSSH refuses private keys whose ACL is too permissive; freshly-created
    /// files inherit the folder ACL, so restrict each restored .ssh file to the current user.
    /// </summary>
    private static async Task TightenAclAsync(string path, Action<string> log, CancellationToken ct)
    {
        var user = $"{Environment.UserDomainName}\\{Environment.UserName}";
        var r = await ProcessRunner.RunAsync(
            "icacls", new[] { path, "/inheritance:r", "/grant:r", $"{user}:F" }, ct).ConfigureAwait(false);
        if (!r.Ok)
            log($"  ! could not tighten permissions on {Path.GetFileName(path)} — ssh may reject it (icacls exit {r.ExitCode})");
    }

    /// <summary>(zip entry name relative to %USERPROFILE%, absolute source path).</summary>
    private static IEnumerable<(string EntryName, string FullPath)> EnumerateSources()
    {
        if (File.Exists(KnownPaths.NpmRc))
            yield return (".npmrc", KnownPaths.NpmRc);

        if (Directory.Exists(KnownPaths.SshDir))
        {
            foreach (var f in Directory.EnumerateFiles(KnownPaths.SshDir, "*", SearchOption.TopDirectoryOnly))
                yield return (Path.Combine(".ssh", Path.GetFileName(f)), f);
        }
    }
}
