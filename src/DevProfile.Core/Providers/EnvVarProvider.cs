using System.Collections;

namespace DevProfile.Core.Providers;

/// <summary>
/// User-scope environment variables. Path/Temp are deliberately excluded — they are
/// machine-specific and merging them is a separate, riskier concern.
/// </summary>
public sealed class EnvVarProvider : IProvider
{
    public string Id => "env-vars";
    public string DisplayName => "User environment variables";
    public ProviderCategory Category => ProviderCategory.Shell;
    public bool ContainsSecrets => false;

    private static readonly HashSet<string> Excluded =
        new(StringComparer.OrdinalIgnoreCase) { "Path", "Temp", "Tmp", "PSModulePath" };

    // Names that strongly suggest a credential. Skipped from the cleartext capture so
    // tokens never land in a profile bundle synced to OneDrive/USB. Their *names* (never
    // values) are recorded so the plan can surface them as manual follow-ups.
    private static readonly string[] SecretMarkers =
        { "TOKEN", "SECRET", "PASSWORD", "PASSWD", "_PAT", "APIKEY", "API_KEY", "CREDENTIAL", "PRIVATE_KEY" };

    private static bool LooksSecret(string name) =>
        SecretMarkers.Any(m => name.Contains(m, StringComparison.OrdinalIgnoreCase));

    private string StorePath(string profileDir) => Path.Combine(profileDir, Id, "user-env.json");
    private string SkippedPath(string profileDir) => Path.Combine(profileDir, Id, "skipped-secrets.json");

    public Task<DiscoveryResult> DiscoverAsync(CancellationToken ct = default)
    {
        var n = Current().Count;
        return Task.FromResult(DiscoveryResult.Found($"{n} variable(s)"));
    }

    public async Task CaptureAsync(string profileDir, ExportOptions options, CancellationToken ct = default)
    {
        var dest = StorePath(profileDir);
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        await File.WriteAllTextAsync(dest, Json.Write(Current()), ct).ConfigureAwait(false);

        var skipped = AllUserVarNames().Where(LooksSecret).OrderBy(x => x).ToList();
        if (skipped.Count > 0)
            await File.WriteAllTextAsync(SkippedPath(profileDir), Json.Write(skipped), ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<PlanItem>> PlanAsync(string profileDir, CancellationToken ct = default)
    {
        var stored = StorePath(profileDir);
        if (!File.Exists(stored)) return Array.Empty<PlanItem>();
        var wanted = Json.Read<Dictionary<string, string>>(await File.ReadAllTextAsync(stored, ct).ConfigureAwait(false))
                     ?? new();
        var current = Current();

        var plan = new List<PlanItem>();
        foreach (var (k, v) in wanted)
        {
            if (!current.TryGetValue(k, out var cur))
                plan.Add(new PlanItem(Id, k, "missing", PlanAction.Install, v));
            else if (!string.Equals(cur, v, StringComparison.Ordinal))
                plan.Add(new PlanItem(Id, k, "differs", PlanAction.Overwrite, v));
            else
                plan.Add(new PlanItem(Id, k, "current", PlanAction.Skip));
        }

        // Credential-looking vars were deliberately not captured; surface them so the
        // user knows to set them by hand on the new machine.
        var skippedPath = SkippedPath(profileDir);
        if (File.Exists(skippedPath))
        {
            var skipped = Json.Read<List<string>>(await File.ReadAllTextAsync(skippedPath, ct).ConfigureAwait(false)) ?? new();
            plan.AddRange(skipped.Select(name =>
                new PlanItem(Id, name, "not captured", PlanAction.Manual, "credential-looking variable — set it by hand")));
        }
        return plan;
    }

    public Task ApplyAsync(string profileDir, PlanItem item, ApplyOptions options, Action<string> log, CancellationToken ct = default)
    {
        // Manual items carry an explanation in Detail, not a value — never write those.
        if (item.Action is not (PlanAction.Install or PlanAction.Overwrite)) return Task.CompletedTask;
        Environment.SetEnvironmentVariable(item.Label, item.Detail, EnvironmentVariableTarget.User);
        log($"  set {item.Label} (user)");
        return Task.CompletedTask;
    }

    private static Dictionary<string, string> Current()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (DictionaryEntry e in Environment.GetEnvironmentVariables(EnvironmentVariableTarget.User))
        {
            var key = (string)e.Key;
            if (Excluded.Contains(key) || LooksSecret(key)) continue;
            result[key] = e.Value?.ToString() ?? "";
        }
        return result;
    }

    private static IEnumerable<string> AllUserVarNames()
    {
        foreach (DictionaryEntry e in Environment.GetEnvironmentVariables(EnvironmentVariableTarget.User))
            yield return (string)e.Key;
    }
}
