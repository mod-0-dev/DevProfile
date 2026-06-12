using System.Text.Json;

namespace DevProfile.Core.Providers;

/// <summary>Packages installed via winget. Reuses winget's own export/import JSON.</summary>
public sealed class WingetProvider : IProvider
{
    public string Id => "winget";
    public string DisplayName => "winget packages";
    public ProviderCategory Category => ProviderCategory.Packages;
    public bool ContainsSecrets => false;

    /// <summary>`winget export` takes several seconds; reuse its result across back-to-back plan builds.</summary>
    private static readonly TimeSpan InstalledCacheTtl = TimeSpan.FromMinutes(2);
    private (DateTime AtUtc, HashSet<string> Ids)? _installedCache;

    private string StorePath(string profileDir) => Path.Combine(profileDir, Id, "packages.json");

    public async Task<DiscoveryResult> DiscoverAsync(CancellationToken ct = default)
    {
        if (!await ProcessRunner.ExistsAsync("winget", ct).ConfigureAwait(false))
            return DiscoveryResult.Missing("winget not on PATH");
        return DiscoveryResult.Found("ready to export");
    }

    public async Task CaptureAsync(string profileDir, ExportOptions options, CancellationToken ct = default)
    {
        var dest = StorePath(profileDir);
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        var r = await ProcessRunner.RunAsync(
            "winget", new[] { "export", "-o", dest, "--accept-source-agreements", "--nowarn" }, ct).ConfigureAwait(false);
        if (!r.Ok && !File.Exists(dest))
            throw new InvalidOperationException($"winget export failed: {r.StdErr}");
    }

    public async Task<IReadOnlyList<PlanItem>> PlanAsync(string profileDir, CancellationToken ct = default)
    {
        var stored = StorePath(profileDir);
        if (!File.Exists(stored)) return Array.Empty<PlanItem>();

        var wanted = ReadIds(File.ReadAllText(stored));
        var installed = await CurrentlyInstalledAsync(ct).ConfigureAwait(false);

        var plan = new List<PlanItem>();
        foreach (var id in wanted)
        {
            if (installed.Contains(id))
                plan.Add(new PlanItem(Id, id, "installed", PlanAction.Skip));
            else if (!LabelValidation.IsWingetId(id))
                plan.Add(new PlanItem(Id, id, "invalid id", PlanAction.Manual, "not a valid winget package id — skipped"));
            else
                plan.Add(new PlanItem(Id, id, "missing", PlanAction.Install));
        }
        return plan;
    }

    public async Task ApplyAsync(string profileDir, PlanItem item, ApplyOptions options, Action<string> log, CancellationToken ct = default)
    {
        if (item.Action != PlanAction.Install) return;
        if (!LabelValidation.IsWingetId(item.Label))
        {
            log($"  ! refusing suspicious package id: {item.Label}");
            return;
        }
        log($"  winget install {item.Label}");
        var r = await ProcessRunner.RunAsync(
            "winget",
            new[]
            {
                "install", "--id", item.Label, "-e", "--source", "winget",
                "--accept-source-agreements", "--accept-package-agreements",
                "--silent", "--disable-interactivity",
            },
            ct).ConfigureAwait(false);
        if (!r.Ok) log($"    ! {r.ShortError()}");
        _installedCache = null; // machine state changed
    }

    private async Task<HashSet<string>> CurrentlyInstalledAsync(CancellationToken ct)
    {
        if (_installedCache is { } cached && DateTime.UtcNow - cached.AtUtc < InstalledCacheTtl)
            return cached.Ids;

        var tmp = Path.Combine(Path.GetTempPath(), $"devprofile-winget-{Guid.NewGuid():N}.json");
        try
        {
            await ProcessRunner.RunAsync(
                "winget", new[] { "export", "-o", tmp, "--accept-source-agreements", "--nowarn" }, ct).ConfigureAwait(false);
            var ids = File.Exists(tmp)
                ? ReadIds(File.ReadAllText(tmp))
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _installedCache = (DateTime.UtcNow, ids);
            return ids;
        }
        finally
        {
            try { File.Delete(tmp); } catch { /* best effort */ }
        }
    }

    /// <summary>Parse PackageIdentifier values out of a winget export JSON.</summary>
    internal static HashSet<string> ReadIds(string json)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("Sources", out var sources))
        {
            foreach (var src in sources.EnumerateArray())
            {
                if (!src.TryGetProperty("Packages", out var pkgs)) continue;
                foreach (var p in pkgs.EnumerateArray())
                {
                    if (p.TryGetProperty("PackageIdentifier", out var pid) && pid.GetString() is { } s)
                        ids.Add(s);
                }
            }
        }
        return ids;
    }
}
