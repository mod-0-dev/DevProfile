namespace DevProfile.Core.Providers;

/// <summary>
/// hosts-file entries. Capture stores the whole file; Apply merges in only the
/// meaningful lines that are missing (never blows away machine-specific entries),
/// backing up first. Writing the hosts file requires elevation.
/// </summary>
public sealed class HostsProvider : IProvider
{
    private readonly string _hostsPath;

    public HostsProvider() : this(KnownPaths.Hosts) { }

    /// <summary>Test seam: point at a fake hosts file instead of the real one.</summary>
    internal HostsProvider(string hostsPath) => _hostsPath = hostsPath;

    public string Id => "hosts";
    public string DisplayName => "hosts entries";
    public ProviderCategory Category => ProviderCategory.GitAndHosts;
    public bool ContainsSecrets => false;

    private string StorePath(string profileDir) => Path.Combine(profileDir, Id, "hosts");

    public Task<DiscoveryResult> DiscoverAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_hostsPath))
            return Task.FromResult(DiscoveryResult.Missing("hosts file not found"));
        var n = MeaningfulLines(File.ReadAllLines(_hostsPath)).Count();
        return Task.FromResult(DiscoveryResult.Found($"{n} entr{(n == 1 ? "y" : "ies")}"));
    }

    public async Task CaptureAsync(string profileDir, ExportOptions options, CancellationToken ct = default)
    {
        if (!File.Exists(_hostsPath)) return;
        var dest = StorePath(profileDir);
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        File.Copy(_hostsPath, dest, overwrite: true);
        await Task.CompletedTask;
    }

    public async Task<IReadOnlyList<PlanItem>> PlanAsync(string profileDir, CancellationToken ct = default)
    {
        var stored = StorePath(profileDir);
        if (!File.Exists(stored)) return Array.Empty<PlanItem>();

        var have = new HashSet<string>(
            MeaningfulLines(await File.ReadAllLinesAsync(stored, ct).ConfigureAwait(false)).Select(Normalize),
            StringComparer.OrdinalIgnoreCase);
        var live = File.Exists(_hostsPath)
            ? new HashSet<string>(MeaningfulLines(File.ReadAllLines(_hostsPath)).Select(Normalize), StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var missing = have.Count(h => !live.Contains(h));
        if (missing == 0)
            return new[] { new PlanItem(Id, DisplayName, "current", PlanAction.Skip) };
        return new[] { new PlanItem(Id, DisplayName, $"{missing} missing", PlanAction.Merge, "appends missing entries (needs admin)") };
    }

    public async Task ApplyAsync(string profileDir, PlanItem item, ApplyOptions options, Action<string> log, CancellationToken ct = default)
    {
        if (item.Action == PlanAction.Skip) return;
        var stored = StorePath(profileDir);
        if (!File.Exists(stored)) return;

        var live = File.Exists(_hostsPath) ? File.ReadAllLines(_hostsPath) : Array.Empty<string>();
        var liveSet = new HashSet<string>(MeaningfulLines(live).Select(Normalize), StringComparer.OrdinalIgnoreCase);
        var toAdd = MeaningfulLines(await File.ReadAllLinesAsync(stored, ct).ConfigureAwait(false))
            .Where(l => !liveSet.Contains(Normalize(l)))
            .ToList();
        if (toAdd.Count == 0) return;

        try
        {
            if (options.BackupOnOverwrite && File.Exists(_hostsPath))
            {
                var bak = _hostsPath + ".devprofile.bak";
                File.Copy(_hostsPath, bak, overwrite: true);
                log($"  backed up hosts -> {bak}");
            }
            var block = new List<string> { "", "# Added by DevProfile" };
            block.AddRange(toAdd);
            await File.AppendAllLinesAsync(_hostsPath, block, ct).ConfigureAwait(false);
            log($"  appended {toAdd.Count} hosts entr{(toAdd.Count == 1 ? "y" : "ies")}");
        }
        catch (UnauthorizedAccessException)
        {
            log("  ! hosts file is read-only — run DevProfile as Administrator to apply hosts entries.");
        }
    }

    private static IEnumerable<string> MeaningfulLines(IEnumerable<string> lines) =>
        lines.Select(l => l.Trim())
             .Where(l => l.Length > 0 && !l.StartsWith('#'));

    private static string Normalize(string line) =>
        string.Join(' ', line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
