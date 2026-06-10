using System.Text.Json;

namespace DevProfile.Core.Providers;

/// <summary>Globally-installed npm packages (npm i -g ...).</summary>
public sealed class NpmGlobalProvider : IProvider
{
    public string Id => "npm-global";
    public string DisplayName => "npm globals";
    public ProviderCategory Category => ProviderCategory.Packages;
    public bool ContainsSecrets => false;

    private string StorePath(string profileDir) => Path.Combine(profileDir, Id, "packages.txt");

    public async Task<DiscoveryResult> DiscoverAsync(CancellationToken ct = default)
    {
        var pkgs = await ListAsync(ct).ConfigureAwait(false);
        if (pkgs is null) return DiscoveryResult.Missing("npm not on PATH");
        return DiscoveryResult.Found($"{pkgs.Count} package(s)");
    }

    public async Task CaptureAsync(string profileDir, ExportOptions options, CancellationToken ct = default)
    {
        // Null means npm itself failed — fail the capture rather than writing an empty
        // list over a possibly-good one in the same profile folder.
        var pkgs = await ListAsync(ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("`npm ls -g` failed — npm missing or broken.");
        var dest = StorePath(profileDir);
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        await File.WriteAllLinesAsync(dest, pkgs.OrderBy(x => x), ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<PlanItem>> PlanAsync(string profileDir, CancellationToken ct = default)
    {
        var stored = StorePath(profileDir);
        if (!File.Exists(stored)) return Array.Empty<PlanItem>();
        var wanted = await File.ReadAllLinesAsync(stored, ct).ConfigureAwait(false);
        var installed = await ListAsync(ct).ConfigureAwait(false) ?? new();
        var have = new HashSet<string>(installed, StringComparer.OrdinalIgnoreCase);

        return wanted
            .Where(w => !string.IsNullOrWhiteSpace(w))
            .Select(w =>
                have.Contains(w) ? new PlanItem(Id, w, "installed", PlanAction.Skip)
                : !LabelValidation.IsNpmPackage(w) ? new PlanItem(Id, w, "invalid name", PlanAction.Manual, "not a valid npm package name — skipped")
                : new PlanItem(Id, w, "missing", PlanAction.Install))
            .ToList();
    }

    public async Task ApplyAsync(string profileDir, PlanItem item, ApplyOptions options, Action<string> log, CancellationToken ct = default)
    {
        if (item.Action != PlanAction.Install) return;
        if (!LabelValidation.IsNpmPackage(item.Label))
        {
            log($"  ! refusing suspicious package name: {item.Label}");
            return;
        }
        log($"  npm i -g {item.Label}");
        var r = await ProcessRunner.RunCmdAsync($"npm install -g {item.Label}", ct).ConfigureAwait(false);
        if (!r.Ok) log($"    ! exit {r.ExitCode}");
    }

    /// <summary>Returns null if npm is unavailable; otherwise the global package names.</summary>
    private static async Task<List<string>?> ListAsync(CancellationToken ct)
    {
        var r = await ProcessRunner.RunCmdAsync("npm ls -g --depth=0 --json", ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(r.StdOut)) return null;
        return ParseList(r.StdOut);
    }

    /// <summary>Parse `npm ls -g --json` output; null if it isn't valid JSON.</summary>
    internal static List<string>? ParseList(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("dependencies", out var deps))
                return new();
            return deps.EnumerateObject()
                .Select(p => p.Name)
                .Where(n => !string.Equals(n, "npm", StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
