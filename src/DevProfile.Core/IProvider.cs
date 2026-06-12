namespace DevProfile.Core;

/// <summary>
/// A capture/restore unit for one slice of a developer machine (winget packages,
/// git config, VS Code, etc.). Each provider owns a subfolder of the profile bundle.
/// </summary>
public interface IProvider
{
    /// <summary>Stable id; also used as the bundle subfolder name. e.g. "winget".</summary>
    string Id { get; }

    /// <summary>Human label shown in the UI. e.g. "winget packages".</summary>
    string DisplayName { get; }

    ProviderCategory Category { get; }

    /// <summary>True if this provider's capture may write secrets and therefore needs a passphrase.</summary>
    bool ContainsSecrets { get; }

    /// <summary>Inspect the current machine: is there anything to capture, and a short detail string.</summary>
    Task<DiscoveryResult> DiscoverAsync(CancellationToken ct = default);

    /// <summary>
    /// Fast precondition check run once, after the PATH refresh, before any of this provider's
    /// items are applied. Return a concrete, actionable reason to skip ALL of them (e.g. the
    /// required CLI isn't on PATH because its runtime was never installed); return null to
    /// proceed. Lets the orchestrator emit one clear message instead of letting each item fail
    /// with a cryptic exit code. Default: always proceed.
    /// </summary>
    Task<string?> PreflightAsync(CancellationToken ct = default) => Task.FromResult<string?>(null);

    /// <summary>Write this provider's state into <paramref name="profileDir"/> (already created).</summary>
    Task CaptureAsync(string profileDir, ExportOptions options, CancellationToken ct = default);

    /// <summary>Compare the captured state against the current machine and return a plan.</summary>
    Task<IReadOnlyList<PlanItem>> PlanAsync(string profileDir, CancellationToken ct = default);

    /// <summary>Apply a single plan item produced by <see cref="PlanAsync"/>.</summary>
    Task ApplyAsync(string profileDir, PlanItem item, ApplyOptions options, Action<string> log, CancellationToken ct = default);
}
