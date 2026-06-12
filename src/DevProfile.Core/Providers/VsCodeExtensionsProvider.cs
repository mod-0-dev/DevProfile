namespace DevProfile.Core.Providers;

/// <summary>Installed VS Code extensions (code --list-extensions / --install-extension).</summary>
public sealed class VsCodeExtensionsProvider : IProvider
{
    public string Id => "vscode-extensions";
    public string DisplayName => "VS Code extensions";
    public ProviderCategory Category => ProviderCategory.VsCode;
    public bool ContainsSecrets => false;

    private string StorePath(string profileDir) => Path.Combine(profileDir, Id, "extensions.txt");

    public async Task<DiscoveryResult> DiscoverAsync(CancellationToken ct = default)
    {
        var ext = await ListAsync(ct).ConfigureAwait(false);
        if (ext is null) return DiscoveryResult.Missing("code not on PATH");
        return DiscoveryResult.Found($"{ext.Count} extension(s)");
    }

    public async Task<string?> PreflightAsync(CancellationToken ct = default) =>
        await ProcessRunner.ExistsAsync("code", ct).ConfigureAwait(false)
            ? null
            : "the code command isn't on PATH — install VS Code (winget id Microsoft.VisualStudioCode) with the \"Add to PATH\" option, then re-apply.";

    public async Task CaptureAsync(string profileDir, ExportOptions options, CancellationToken ct = default)
    {
        var ext = await ListAsync(ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("`code --list-extensions` failed — VS Code missing or broken.");
        var dest = StorePath(profileDir);
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        await File.WriteAllLinesAsync(dest, ext.OrderBy(x => x, StringComparer.OrdinalIgnoreCase), ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<PlanItem>> PlanAsync(string profileDir, CancellationToken ct = default)
    {
        var stored = StorePath(profileDir);
        if (!File.Exists(stored)) return Array.Empty<PlanItem>();
        var wanted = await File.ReadAllLinesAsync(stored, ct).ConfigureAwait(false);
        var have = new HashSet<string>(await ListAsync(ct).ConfigureAwait(false) ?? new(), StringComparer.OrdinalIgnoreCase);

        return wanted
            .Where(w => !string.IsNullOrWhiteSpace(w))
            .Select(w =>
                have.Contains(w) ? new PlanItem(Id, w, "installed", PlanAction.Skip)
                : !LabelValidation.IsVsCodeExtension(w) ? new PlanItem(Id, w, "invalid id", PlanAction.Manual, "not a valid extension id — skipped")
                : new PlanItem(Id, w, "missing", PlanAction.Install))
            .ToList();
    }

    public async Task ApplyAsync(string profileDir, PlanItem item, ApplyOptions options, Action<string> log, CancellationToken ct = default)
    {
        if (item.Action != PlanAction.Install) return;
        if (!LabelValidation.IsVsCodeExtension(item.Label))
        {
            log($"  ! refusing suspicious extension id: {item.Label}");
            return;
        }
        log($"  code --install-extension {item.Label}");
        var r = await ProcessRunner.RunCmdAsync($"code --install-extension {item.Label} --force", ct).ConfigureAwait(false);
        if (!r.Ok) log($"    ! {r.ShortError()}");
    }

    private static async Task<List<string>?> ListAsync(CancellationToken ct)
    {
        var r = await ProcessRunner.RunCmdAsync("code --list-extensions", ct).ConfigureAwait(false);
        if (r.ExitCode != 0 && string.IsNullOrWhiteSpace(r.StdOut)) return null;
        return ParseExtensions(r.StdOut);
    }

    /// <summary>
    /// Keep only lines shaped like "publisher.name" — `code` mixes startup warnings
    /// (GPU, update checks) into stdout and those must not be captured as extensions.
    /// </summary>
    internal static List<string> ParseExtensions(string stdout) =>
        stdout
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(LabelValidation.IsVsCodeExtension)
            .ToList();
}
