namespace DevProfile.Core;

/// <summary>Top-level grouping shown on the Create screen.</summary>
public enum ProviderCategory
{
    Packages,
    GitAndHosts,
    VsCode,
    Shell,
    Secrets,
}

/// <summary>What an Apply step will do to a single item.</summary>
public enum PlanAction
{
    Install,    // not present on this machine -> add it
    Overwrite,  // present but differs -> replace (backup taken first)
    Merge,      // present -> append/merge missing pieces, keep what's there (hosts)
    Skip,       // already current -> nothing to do
    Manual,     // captured, but the user must finish by hand (e.g. excluded secret)
}

/// <summary>Result of asking a provider what it can capture from the current machine.</summary>
public sealed record DiscoveryResult(bool Available, string? Detail)
{
    public static DiscoveryResult Missing(string reason) => new(false, reason);
    public static DiscoveryResult Found(string detail) => new(true, detail);
}

/// <summary>One row in the Apply preview.</summary>
public sealed record PlanItem(
    string ProviderId,
    string Label,
    string Status,
    PlanAction Action,
    string? Detail = null);

/// <summary>Options passed through an export run.</summary>
public sealed record ExportOptions(string? Passphrase = null);

/// <summary>Options passed through an apply run.</summary>
public sealed record ApplyOptions(string? Passphrase = null, bool BackupOnOverwrite = true);

/// <summary>profile.json — the manifest written at the root of a profile bundle.</summary>
public sealed class ProfileManifest
{
    public string Schema { get; set; } = "devprofile/v1";
    public string Name { get; set; } = "MyProfile";
    public string CreatedUtc { get; set; } = "";
    public string SourceMachine { get; set; } = "";
    public string SourceUser { get; set; } = "";
    /// <summary>Provider ids included in this bundle.</summary>
    public List<string> Providers { get; set; } = new();
    public bool ContainsEncryptedSecrets { get; set; }
}
