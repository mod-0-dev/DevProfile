using System.Reflection;
using DevProfile.Core;

namespace DevProfile.Cli;

/// <summary>Thrown for bad invocations: prints the message + usage and exits 2.</summary>
internal sealed class UsageException(string message) : Exception(message);

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        // When relaunched elevated to write the hosts file, do only that and exit.
        if (HostsElevation.TryHandle(args, out var hostsExit)) return hostsExit;

        Console.OutputEncoding = System.Text.Encoding.UTF8;
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        try
        {
            var (command, folder, options) = Parse(args);
            return command switch
            {
                "list" => await Commands.ListAsync(cts.Token),
                "export" => await Commands.ExportAsync(RequireFolder(command, folder), options, cts.Token),
                "refresh" => await Commands.RefreshAsync(RequireFolder(command, folder), options, cts.Token),
                "plan" => await Commands.PlanAsync(RequireFolder(command, folder), cts.Token),
                "apply" => await Commands.ApplyAsync(RequireFolder(command, folder), options, cts.Token),
                "help" or "--help" or "-h" => PrintUsage(),
                "--version" => PrintVersion(),
                _ => throw new UsageException($"unknown command \"{command}\"."),
            };
        }
        catch (UsageException ex)
        {
            ConsoleUi.Error(ex.Message);
            PrintUsage();
            return 2;
        }
        catch (OperationCanceledException)
        {
            ConsoleUi.Line(ConsoleColor.Yellow, "Cancelled.");
            return 1;
        }
        catch (Exception ex)
        {
            ConsoleUi.Error(ex.Message);
            return 1;
        }
    }

    private static string RequireFolder(string command, string? folder) =>
        folder ?? throw new UsageException($"\"{command}\" needs a profile folder argument.");

    /// <summary>Options accepted after the command; everything else positional is command + folder.</summary>
    private static readonly HashSet<string> ValueOptions =
        new(StringComparer.OrdinalIgnoreCase) { "--include", "--exclude", "--only", "--skip", "--passphrase-env" };
    private static readonly HashSet<string> FlagOptions =
        new(StringComparer.OrdinalIgnoreCase) { "--yes", "-y", "--secrets", "--no-backup" };

    internal sealed record CliOptions(
        IReadOnlyList<string>? Include,
        IReadOnlyList<string>? Exclude,
        IReadOnlyList<string>? Only,
        IReadOnlyList<string>? Skip,
        bool Yes,
        bool Secrets,
        bool NoBackup,
        string? PassphraseEnv);

    private static (string Command, string? Folder, CliOptions Options) Parse(string[] args)
    {
        if (args.Length == 0) throw new UsageException("no command given.");

        var positional = new List<string>();
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (ValueOptions.Contains(a))
            {
                if (i + 1 >= args.Length) throw new UsageException($"{a} needs a value.");
                values[a] = args[++i];
            }
            else if (FlagOptions.Contains(a)) flags.Add(a);
            else if (a.StartsWith('-') && positional.Count > 0) throw new UsageException($"unknown option \"{a}\".");
            else positional.Add(a);
        }

        IReadOnlyList<string>? Ids(string key) =>
            values.TryGetValue(key, out var v)
                ? v.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                : null;

        var options = new CliOptions(
            Include: Ids("--include"),
            Exclude: Ids("--exclude"),
            Only: Ids("--only"),
            Skip: Ids("--skip"),
            Yes: flags.Contains("--yes") || flags.Contains("-y"),
            Secrets: flags.Contains("--secrets"),
            NoBackup: flags.Contains("--no-backup"),
            PassphraseEnv: values.GetValueOrDefault("--passphrase-env"));

        return (positional[0].ToLowerInvariant(), positional.Count > 1 ? positional[1] : null, options);
    }

    private static int PrintVersion()
    {
        var version = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
        ConsoleUi.Line($"devprofile {version}");
        return 0;
    }

    private static int PrintUsage()
    {
        ConsoleUi.Line("""

            devprofile — portable developer-environment snapshots for Windows

            usage:
              devprofile list                      providers and what they'd capture from this machine
              devprofile export  <folder> [opts]   capture a new profile into <folder>
              devprofile refresh <folder> [opts]   re-capture an existing profile in place
              devprofile plan    <folder>          preview what apply would do on this machine
              devprofile apply   <folder> [opts]   apply a profile to this machine

            export options:
              --include <ids>          comma-separated provider ids (default: everything available)
              --exclude <ids>          drop providers from the default selection
              --secrets                also capture the encrypted secrets bundle (prompts for passphrase)

            apply options:
              --yes, -y                apply without asking for confirmation
              --only <ids>             apply only these providers
              --skip <ids>             apply everything except these providers
              --no-backup              don't write *.devprofile.bak backups before overwrites

            common options:
              --passphrase-env <VAR>   read the secrets passphrase from this environment variable
                                       (required when running non-interactively)

            exit codes: 0 success · 1 something failed · 2 bad usage
            """);
        return 0;
    }
}
