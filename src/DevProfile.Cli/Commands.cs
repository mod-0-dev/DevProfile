using DevProfile.Core;
using static DevProfile.Cli.Program;

namespace DevProfile.Cli;

internal static class Commands
{
    public static async Task<int> ListAsync(CancellationToken ct)
    {
        var service = new ProfileService();
        var discovered = await Task.WhenAll(service.Providers.Select(async p =>
        {
            try { return (Provider: p, Result: await p.DiscoverAsync(ct)); }
            catch (Exception ex) { return (Provider: p, Result: DiscoveryResult.Missing(ex.Message)); }
        }));

        int wId = discovered.Max(d => d.Provider.Id.Length);
        foreach (var (provider, result) in discovered)
        {
            Console.ForegroundColor = result.Available ? ConsoleColor.Green : ConsoleColor.DarkGray;
            Console.Write(result.Available ? "  ✓ " : "  - ");
            Console.ResetColor();
            Console.Write($"{provider.Id.PadRight(wId)}  ");
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine(result.Detail ?? "");
            Console.ResetColor();
        }
        return 0;
    }

    public static async Task<int> ExportAsync(string folder, CliOptions options, CancellationToken ct)
    {
        var service = new ProfileService();
        var known = service.Providers.Select(p => p.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        ValidateIds(options.Include, known);
        ValidateIds(options.Exclude, known);

        var available = (await Task.WhenAll(service.Providers.Select(async p =>
        {
            try { return (await p.DiscoverAsync(ct)).Available ? p.Id : null; }
            catch { return null; }
        }))).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);

        List<string> selected;
        if (options.Include is not null)
        {
            var missing = options.Include.Where(id => !available.Contains(id)).ToList();
            if (missing.Count > 0)
                ConsoleUi.Line(ConsoleColor.Yellow, $"  skipping (nothing found to capture): {string.Join(", ", missing)}");
            selected = options.Include.Where(available.Contains).ToList();
        }
        else
        {
            // Default: everything discoverable; secrets stay opt-in.
            selected = service.Providers
                .Where(p => available.Contains(p.Id) && (options.Secrets || !p.ContainsSecrets))
                .Select(p => p.Id)
                .ToList();
        }
        if (options.Exclude is not null)
            selected.RemoveAll(id => options.Exclude.Contains(id, StringComparer.OrdinalIgnoreCase));
        if (selected.Count == 0) throw new UsageException("nothing selected to export.");

        var needsSecrets = selected.Any(id => service.Find(id)!.ContainsSecrets);
        var passphrase = needsSecrets ? ResolvePassphrase(options, "Secrets passphrase") : null;

        await service.ExportAsync(folder, selected, new ExportOptions(passphrase), ConsoleUi.LogLine, ct);
        ConsoleUi.Line(ConsoleColor.Green, $"Profile written to: {Path.GetFullPath(folder)}");
        return 0;
    }

    public static async Task<int> RefreshAsync(string folder, CliOptions options, CancellationToken ct)
    {
        var service = new ProfileService();
        var manifest = await service.ReadManifestAsync(folder, ct)
            ?? throw new InvalidDataException("No profile.json found — this folder is not a DevProfile bundle.");

        var passphrase = manifest.Providers.Contains("secrets")
            ? ResolvePassphrase(options, "Secrets passphrase")
            : null;

        await service.RefreshAsync(folder, new ExportOptions(passphrase), ConsoleUi.LogLine, ct);
        ConsoleUi.Line(ConsoleColor.Green, $"Profile \"{manifest.Name}\" refreshed from this machine.");
        return 0;
    }

    public static async Task<int> PlanAsync(string folder, CancellationToken ct)
    {
        var service = new ProfileService();
        var items = await service.BuildPlanAsync(folder, ct);
        ConsoleUi.RenderPlan(items);
        ConsoleUi.Line();
        ConsoleUi.Line(ConsoleUi.Summarize(items));
        return 0;
    }

    public static async Task<int> ApplyAsync(string folder, CliOptions options, CancellationToken ct)
    {
        var service = new ProfileService();
        var known = service.Providers.Select(p => p.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        ValidateIds(options.Only, known);
        ValidateIds(options.Skip, known);

        var items = await service.BuildPlanAsync(folder, ct);
        var actionable = items
            .Where(i => i.Action is PlanAction.Install or PlanAction.Overwrite or PlanAction.Merge)
            .Where(i => options.Only is null || options.Only.Contains(i.ProviderId, StringComparer.OrdinalIgnoreCase))
            .Where(i => options.Skip is null || !options.Skip.Contains(i.ProviderId, StringComparer.OrdinalIgnoreCase))
            .ToList();

        ConsoleUi.RenderPlan(items);
        ConsoleUi.Line();
        ConsoleUi.Line(ConsoleUi.Summarize(items));
        if (actionable.Count == 0)
        {
            ConsoleUi.Line(ConsoleColor.Green, "Nothing to apply — this machine is already current.");
            return 0;
        }

        if (!options.Yes)
        {
            if (Console.IsInputRedirected)
                throw new UsageException("confirmation needed — pass --yes when running non-interactively.");
            if (!ConsoleUi.Confirm($"Apply {actionable.Count} item(s)?")) { ConsoleUi.Line("Aborted."); return 1; }
        }

        var passphrase = actionable.Any(i => i.ProviderId == "secrets")
            ? ResolvePassphrase(options, "Secrets passphrase")
            : null;

        var result = await service.ApplyAsync(
            folder, actionable, new ApplyOptions(passphrase, BackupOnOverwrite: !options.NoBackup),
            ConsoleUi.LogLine, ct);

        var color = result.Ok ? ConsoleColor.Green : ConsoleColor.Yellow;
        ConsoleUi.Line(color, $"{result.Applied} applied · {result.Failed} failed · {result.SkippedByPreflight} skipped (missing prerequisite)");
        return result.Ok ? 0 : 1;
    }

    private static void ValidateIds(IReadOnlyList<string>? ids, HashSet<string> known)
    {
        var unknown = ids?.Where(id => !known.Contains(id)).ToList();
        if (unknown is { Count: > 0 })
            throw new UsageException(
                $"unknown provider id(s): {string.Join(", ", unknown)}. Run \"devprofile list\" to see valid ids.");
    }

    private static string ResolvePassphrase(CliOptions options, string prompt)
    {
        if (options.PassphraseEnv is not null)
        {
            var value = Environment.GetEnvironmentVariable(options.PassphraseEnv);
            if (string.IsNullOrEmpty(value))
                throw new UsageException($"environment variable {options.PassphraseEnv} is unset or empty.");
            return value;
        }
        if (Console.IsInputRedirected)
            throw new UsageException("a secrets passphrase is needed — pass --passphrase-env <VAR> when running non-interactively.");
        return ConsoleUi.PromptPassphrase(prompt);
    }
}
