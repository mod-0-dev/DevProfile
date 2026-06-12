using DevProfile.Core.Providers;

namespace DevProfile.Core;

/// <summary>
/// Orchestrates capture (export) and restore (plan + apply) across all providers.
/// The provider list here is the single source of truth for what DevProfile handles.
/// </summary>
public sealed class ProfileService
{
    public IReadOnlyList<IProvider> Providers { get; }

    public ProfileService(IEnumerable<IProvider>? providers = null)
    {
        Providers = (providers ?? DefaultProviders()).ToList();
    }

    public static IEnumerable<IProvider> DefaultProviders() => new IProvider[]
    {
        new WingetProvider(),
        new NpmGlobalProvider(),
        new DotnetToolProvider(),
        new GitConfigProvider(),
        new HostsProvider(),
        new VsCodeSettingsProvider(),
        new VsCodeExtensionsProvider(),
        new PowerShellProfileProvider(),
        new WindowsTerminalProvider(),
        new EnvVarProvider(),
        new SecretsProvider(),
    };

    public IProvider? Find(string id) => Providers.FirstOrDefault(p => p.Id == id);

    /// <summary>Export the selected providers into <paramref name="profileDir"/> and write the manifest.</summary>
    public async Task ExportAsync(
        string profileDir,
        IReadOnlyCollection<string> selectedProviderIds,
        ExportOptions options,
        Action<string> log,
        CancellationToken ct = default)
    {
        Directory.CreateDirectory(profileDir);
        var included = new List<string>();

        foreach (var provider in Providers.Where(p => selectedProviderIds.Contains(p.Id)))
        {
            ct.ThrowIfCancellationRequested();
            log($"Capturing {provider.DisplayName}…");
            try
            {
                await provider.CaptureAsync(profileDir, options, ct).ConfigureAwait(false);
                included.Add(provider.Id);
            }
            catch (Exception ex)
            {
                log($"  ! {provider.DisplayName} failed: {ex.Message}");
            }
        }

        var manifest = new ProfileManifest
        {
            Name = new DirectoryInfo(profileDir).Name,
            CreatedUtc = DateTime.UtcNow.ToString("O"),
            SourceMachine = Environment.MachineName,
            SourceUser = Environment.UserName,
            Providers = included,
            ContainsEncryptedSecrets = included.Contains("secrets"),
        };
        await File.WriteAllTextAsync(Path.Combine(profileDir, "profile.json"), Json.Write(manifest), ct).ConfigureAwait(false);
        log("Done.");
    }

    public const string SupportedSchema = "devprofile/v1";

    public async Task<ProfileManifest?> ReadManifestAsync(string profileDir, CancellationToken ct = default)
    {
        var path = Path.Combine(profileDir, "profile.json");
        if (!File.Exists(path)) return null;
        var manifest = Json.Read<ProfileManifest>(await File.ReadAllTextAsync(path, ct).ConfigureAwait(false));
        if (manifest is not null && manifest.Schema != SupportedSchema)
            throw new InvalidDataException(
                $"Profile schema \"{manifest.Schema}\" is not supported (expected \"{SupportedSchema}\").");
        return manifest;
    }

    /// <summary>Build the full Apply preview by asking every captured provider for its plan.</summary>
    public async Task<IReadOnlyList<PlanItem>> BuildPlanAsync(string profileDir, CancellationToken ct = default)
    {
        // No manifest -> not a profile. Refuse rather than scavenging whatever stray
        // files happen to match provider folder names in an arbitrary directory.
        var manifest = await ReadManifestAsync(profileDir, ct).ConfigureAwait(false)
            ?? throw new InvalidDataException("No profile.json found — this folder is not a DevProfile bundle.");
        var ids = manifest.Providers;

        var all = new List<PlanItem>();
        foreach (var provider in Providers.Where(p => ids.Contains(p.Id)))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                all.AddRange(await provider.PlanAsync(profileDir, ct).ConfigureAwait(false));
            }
            catch (Exception ex)
            {
                all.Add(new PlanItem(provider.Id, provider.DisplayName, "error", PlanAction.Manual, ex.Message));
            }
        }
        return all;
    }

    /// <summary>
    /// Apply runs in dependency phases:
    ///  <list type="number">
    ///   <item>tool installers (winget) — provide the runtimes later steps shell out to;
    ///         afterwards PATH is refreshed in-process so code/npm/dotnet become callable.</item>
    ///   <item>credentials those steps read — secrets restores <c>~/.npmrc</c>, so it must land
    ///         before <c>npm i -g</c> can need a private-registry token from it.</item>
    ///   <item>everything else: the tool consumers (npm/dotnet/code) and the config file copies,
    ///         which are mutually independent.</item>
    ///  </list>
    /// </summary>
    private static readonly HashSet<string> ToolInstallerIds =
        new(StringComparer.OrdinalIgnoreCase) { "winget" };
    private static readonly HashSet<string> CredentialIds =
        new(StringComparer.OrdinalIgnoreCase) { "secrets" };

    private static int Phase(string providerId) =>
        ToolInstallerIds.Contains(providerId) ? 0
        : CredentialIds.Contains(providerId) ? 1
        : 2;

    /// <summary>Apply the given plan items (typically the non-Skip ones the user kept ticked).</summary>
    public async Task ApplyAsync(
        string profileDir,
        IEnumerable<PlanItem> items,
        ApplyOptions options,
        Action<string> log,
        CancellationToken ct = default)
    {
        // OrderBy is a stable sort, so original order is preserved within each phase.
        var ordered = items.OrderBy(i => Phase(i.ProviderId)).ToList();
        var refreshed = false;
        var preflighted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var skip = new HashSet<string>(StringComparer.OrdinalIgnoreCase); // failed preflight -> all items skipped

        foreach (var item in ordered)
        {
            ct.ThrowIfCancellationRequested();

            // Crossing from the tool-installer phase into the rest: refresh PATH once so
            // freshly-installed code/node/dotnet are visible to the steps that need them.
            if (!refreshed && Phase(item.ProviderId) > 0)
            {
                EnvironmentRefresher.Refresh();
                log("Refreshed PATH from registry (picking up newly-installed tools)…");
                refreshed = true;
            }

            var provider = Find(item.ProviderId);
            if (provider is null) continue;

            // Preflight once per provider (now that PATH is refreshed, so a just-installed tool
            // counts): if its prerequisite is missing, skip ALL its items with one concrete
            // message instead of letting each fail with a cryptic exit code.
            if (preflighted.Add(item.ProviderId))
            {
                string? reason;
                try { reason = await provider.PreflightAsync(ct).ConfigureAwait(false); }
                catch (Exception ex) { reason = ex.Message; }
                if (reason is not null)
                {
                    skip.Add(item.ProviderId);
                    int n = ordered.Count(i => string.Equals(i.ProviderId, item.ProviderId, StringComparison.OrdinalIgnoreCase));
                    log($"  ! Skipping {provider.DisplayName} — {reason} ({n} item(s) not applied)");
                }
            }
            if (skip.Contains(item.ProviderId)) continue;

            try
            {
                await provider.ApplyAsync(profileDir, item, options, log, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                log($"  ! {item.Label}: {ex.Message}");
            }
        }
        log("Apply complete.");
    }
}
