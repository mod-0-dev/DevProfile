namespace DevProfile.Core.Providers;

/// <summary>Globally-installed .NET tools (dotnet tool install -g ...).</summary>
public sealed class DotnetToolProvider : IProvider
{
    public string Id => "dotnet-tools";
    public string DisplayName => ".NET global tools";
    public ProviderCategory Category => ProviderCategory.Packages;
    public bool ContainsSecrets => false;

    private string StorePath(string profileDir) => Path.Combine(profileDir, Id, "tools.txt");

    public async Task<DiscoveryResult> DiscoverAsync(CancellationToken ct = default)
    {
        if (!await ProcessRunner.ExistsAsync("dotnet", ct).ConfigureAwait(false))
            return DiscoveryResult.Missing("dotnet not on PATH");
        var tools = await ListAsync(ct).ConfigureAwait(false);
        if (tools is null) return DiscoveryResult.Missing("`dotnet tool list` failed");
        return DiscoveryResult.Found($"{tools.Count} tool(s)");
    }

    public async Task<string?> PreflightAsync(CancellationToken ct = default)
    {
        if (!await ProcessRunner.ExistsAsync("dotnet", ct).ConfigureAwait(false))
            return "the dotnet command isn't on PATH — install the .NET SDK (winget id Microsoft.DotNet.SDK.10), then re-apply.";

        // `dotnet` ships with the runtime alone, but `dotnet tool install` needs an SDK — the
        // friend's machine had the runtime (enough to run DevProfile) and no SDK. An empty
        // --list-sdks (it still exits 0) means runtime-only. On any oddity, proceed rather than
        // wrongly skip; the real error then surfaces via ProcessResult.ShortError().
        var r = await ProcessRunner.RunAsync("dotnet", new[] { "--list-sdks" }, ct).ConfigureAwait(false);
        if (r.Ok && string.IsNullOrWhiteSpace(r.StdOut))
            return "only the .NET runtime is installed, not the SDK — `dotnet tool install` needs the SDK (winget id Microsoft.DotNet.SDK.10).";
        return null;
    }

    public async Task CaptureAsync(string profileDir, ExportOptions options, CancellationToken ct = default)
    {
        var tools = await ListAsync(ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("`dotnet tool list --global` failed.");
        var dest = StorePath(profileDir);
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        await File.WriteAllLinesAsync(dest, tools.OrderBy(x => x), ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<PlanItem>> PlanAsync(string profileDir, CancellationToken ct = default)
    {
        var stored = StorePath(profileDir);
        if (!File.Exists(stored)) return Array.Empty<PlanItem>();
        var wanted = await File.ReadAllLinesAsync(stored, ct).ConfigureAwait(false);
        var have = new HashSet<string>(
            await ListAsync(ct).ConfigureAwait(false) ?? new(), StringComparer.OrdinalIgnoreCase);

        return wanted
            .Where(w => !string.IsNullOrWhiteSpace(w))
            .Select(w =>
                have.Contains(w) ? new PlanItem(Id, w, "installed", PlanAction.Skip)
                : !LabelValidation.IsDotnetToolId(w) ? new PlanItem(Id, w, "invalid id", PlanAction.Manual, "not a valid tool package id — skipped")
                : new PlanItem(Id, w, "missing", PlanAction.Install))
            .ToList();
    }

    public async Task ApplyAsync(string profileDir, PlanItem item, ApplyOptions options, Action<string> log, CancellationToken ct = default)
    {
        if (item.Action != PlanAction.Install) return;
        if (!LabelValidation.IsDotnetToolId(item.Label))
        {
            log($"  ! refusing suspicious tool id: {item.Label}");
            return;
        }
        log($"  dotnet tool install -g {item.Label}");
        var r = await ProcessRunner.RunAsync(
            "dotnet", new[] { "tool", "install", "-g", item.Label }, ct).ConfigureAwait(false);
        if (!r.Ok) log($"    ! {r.ShortError()}");
    }

    /// <summary>Null when the command itself failed (as opposed to listing zero tools).</summary>
    private static async Task<List<string>?> ListAsync(CancellationToken ct)
    {
        var r = await ProcessRunner.RunAsync("dotnet", "tool list --global", ct).ConfigureAwait(false);
        if (!r.Ok) return null;
        return ParseToolList(r.StdOut);
    }

    /// <summary>Parse `dotnet tool list --global` table; first column is the package id.</summary>
    internal static List<string> ParseToolList(string stdout)
    {
        var ids = new List<string>();
        var lines = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        // Skip the header row ("Package Id ... Version ... Commands") and the "----" separator.
        bool pastSeparator = false;
        foreach (var line in lines)
        {
            if (!pastSeparator)
            {
                if (line.StartsWith("---")) pastSeparator = true;
                continue;
            }
            var id = line.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(id)) ids.Add(id);
        }
        return ids;
    }
}
