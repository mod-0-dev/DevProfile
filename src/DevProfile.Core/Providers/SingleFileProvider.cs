using System.Security.Cryptography;

namespace DevProfile.Core.Providers;

/// <summary>
/// Base for providers that capture exactly one config file (git config, shell profile,
/// terminal settings, VS Code settings) and restore it with a backup-on-overwrite.
/// </summary>
public abstract class SingleFileProvider : IProvider
{
    public abstract string Id { get; }
    public abstract string DisplayName { get; }
    public abstract ProviderCategory Category { get; }
    public virtual bool ContainsSecrets => false;

    /// <summary>Absolute path of the live file on this machine (may not exist).</summary>
    protected abstract string LivePath { get; }

    /// <summary>File name used inside the provider's bundle folder.</summary>
    protected virtual string StoredFileName => Path.GetFileName(LivePath);

    private string StoredPath(string profileDir) => Path.Combine(profileDir, Id, StoredFileName);

    public Task<DiscoveryResult> DiscoverAsync(CancellationToken ct = default)
    {
        if (!File.Exists(LivePath))
            return Task.FromResult(DiscoveryResult.Missing("not found"));
        var info = new FileInfo(LivePath);
        return Task.FromResult(DiscoveryResult.Found($"{info.Length:N0} bytes"));
    }

    public async Task CaptureAsync(string profileDir, ExportOptions options, CancellationToken ct = default)
    {
        if (!File.Exists(LivePath)) return;
        var dest = StoredPath(profileDir);
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        await CopyAsync(LivePath, dest, ct).ConfigureAwait(false);
    }

    public Task<IReadOnlyList<PlanItem>> PlanAsync(string profileDir, CancellationToken ct = default)
    {
        var stored = StoredPath(profileDir);
        IReadOnlyList<PlanItem> result;
        if (!File.Exists(stored))
        {
            result = Array.Empty<PlanItem>();
        }
        else if (!File.Exists(LivePath))
        {
            result = new[] { new PlanItem(Id, DisplayName, "missing", PlanAction.Install, LivePath) };
        }
        else if (FilesEqual(stored, LivePath))
        {
            result = new[] { new PlanItem(Id, DisplayName, "current", PlanAction.Skip, LivePath) };
        }
        else
        {
            result = new[] { new PlanItem(Id, DisplayName, "differs", PlanAction.Overwrite, LivePath) };
        }
        return Task.FromResult(result);
    }

    public async Task ApplyAsync(string profileDir, PlanItem item, ApplyOptions options, Action<string> log, CancellationToken ct = default)
    {
        if (item.Action == PlanAction.Skip) return;
        var stored = StoredPath(profileDir);
        if (!File.Exists(stored)) return;

        Directory.CreateDirectory(Path.GetDirectoryName(LivePath)!);
        if (item.Action == PlanAction.Overwrite && options.BackupOnOverwrite && File.Exists(LivePath))
        {
            var bak = LivePath + ".devprofile.bak";
            File.Copy(LivePath, bak, overwrite: true);
            log($"  backed up existing -> {bak}");
        }
        await CopyAsync(stored, LivePath, ct).ConfigureAwait(false);
        log($"  wrote {LivePath}");
    }

    protected static async Task CopyAsync(string src, string dest, CancellationToken ct)
    {
        await using var rs = File.OpenRead(src);
        await using var ws = File.Create(dest);
        await rs.CopyToAsync(ws, ct).ConfigureAwait(false);
    }

    private static bool FilesEqual(string a, string b) =>
        SHA256.HashData(File.ReadAllBytes(a)).AsSpan().SequenceEqual(SHA256.HashData(File.ReadAllBytes(b)));
}
