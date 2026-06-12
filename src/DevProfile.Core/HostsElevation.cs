using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;

namespace DevProfile.Core;

/// <summary>
/// Mid-apply elevation handoff for the hosts file, so applying hosts entries shows one UAC
/// prompt instead of requiring the whole app to restart as Administrator.
///
/// The un-elevated apply process writes the lines to append into a temp payload file, then
/// relaunches this same executable elevated with "--elevated-hosts &lt;payload&gt; &lt;sha256&gt;".
/// The elevated instance verifies the payload against the hash from the command line (the
/// command line of a running elevated process can't be altered, so a payload file swapped
/// after launch is detected), re-validates every line as a hosts entry, and appends to the
/// real hosts file — the destination is fixed, never taken from the payload.
/// </summary>
public static class HostsElevation
{
    public const string Switch = "--elevated-hosts";

    // Elevated-child exit codes, mapped back to messages in the parent.
    private const int ExitOk = 0;
    private const int ExitBadPayload = 2;
    private const int ExitWriteFailed = 3;
    private const int ExitHashMismatch = 4;

    private sealed class Payload
    {
        public List<string> Lines { get; set; } = new();
        public bool Backup { get; set; } = true;
    }

    /// <summary>
    /// Front-end entry point: call first thing in Main/OnStartup. Returns true when this
    /// process was launched as the elevated hosts writer; the caller must exit with
    /// <paramref name="exitCode"/> without showing any UI.
    /// </summary>
    public static bool TryHandle(string[] args, out int exitCode)
    {
        var idx = Array.IndexOf(args, Switch);
        if (idx < 0 || idx + 2 >= args.Length) { exitCode = 0; return false; }
        exitCode = RunElevated(args[idx + 1], args[idx + 2], KnownPaths.Hosts);
        return true;
    }

    /// <summary>The elevated side. Internal overload with a hosts-path seam for tests.</summary>
    internal static int RunElevated(string payloadPath, string expectedSha256Hex, string hostsPath)
    {
        Payload? payload;
        try
        {
            var bytes = File.ReadAllBytes(payloadPath);
            var hash = Convert.ToHexString(SHA256.HashData(bytes));
            if (!hash.Equals(expectedSha256Hex, StringComparison.OrdinalIgnoreCase))
                return ExitHashMismatch;
            payload = Json.Read<Payload>(System.Text.Encoding.UTF8.GetString(bytes));
        }
        catch
        {
            return ExitBadPayload;
        }
        if (payload is null || payload.Lines.Count == 0 || !payload.Lines.All(IsHostsEntry))
            return ExitBadPayload;

        try
        {
            // Dedupe against the live file again — it may have changed between plan and UAC consent.
            var liveSet = File.Exists(hostsPath)
                ? new HashSet<string>(
                    HostsText.MeaningfulLines(File.ReadAllLines(hostsPath)).Select(HostsText.Normalize),
                    StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var toAdd = payload.Lines.Where(l => !liveSet.Contains(HostsText.Normalize(l))).ToList();
            if (toAdd.Count == 0) return ExitOk;

            if (payload.Backup && File.Exists(hostsPath))
                File.Copy(hostsPath, hostsPath + ".devprofile.bak", overwrite: true);

            var block = new List<string> { "", "# Added by DevProfile" };
            block.AddRange(toAdd);
            File.AppendAllLines(hostsPath, block);
            return ExitOk;
        }
        catch
        {
            return ExitWriteFailed;
        }
    }

    /// <summary>
    /// The un-elevated side: spawn the elevated child and wait. Throws with an actionable
    /// message when the user declines UAC or the child reports failure, so the orchestrator
    /// logs it and counts the item as failed.
    /// </summary>
    public static async Task ApplyViaElevatedChildAsync(
        IReadOnlyList<string> lines,
        bool backup,
        Action<string> log,
        CancellationToken ct = default)
    {
        var exe = Environment.ProcessPath
            ?? throw new InvalidOperationException("cannot locate the current executable to elevate.");

        var payloadPath = Path.Combine(Path.GetTempPath(), $"devprofile-hosts-{Guid.NewGuid():N}.json");
        var bytes = System.Text.Encoding.UTF8.GetBytes(
            Json.Write(new Payload { Lines = lines.ToList(), Backup = backup }));
        await File.WriteAllBytesAsync(payloadPath, bytes, ct).ConfigureAwait(false);
        var hash = Convert.ToHexString(SHA256.HashData(bytes));

        try
        {
            var psi = new ProcessStartInfo(exe) { UseShellExecute = true, Verb = "runas" };
            psi.ArgumentList.Add(Switch);
            psi.ArgumentList.Add(payloadPath);
            psi.ArgumentList.Add(hash);

            Process proc;
            try
            {
                proc = Process.Start(psi)
                    ?? throw new InvalidOperationException("elevation request did not start.");
            }
            catch (Win32Exception) // ERROR_CANCELLED: the user dismissed the UAC prompt
            {
                throw new InvalidOperationException("elevation declined — hosts entries not applied.");
            }

            using (proc)
            {
                await proc.WaitForExitAsync(ct).ConfigureAwait(false);
                switch (proc.ExitCode)
                {
                    case ExitOk:
                        log($"  appended {lines.Count} hosts entr{(lines.Count == 1 ? "y" : "ies")} (elevated)");
                        return;
                    case ExitHashMismatch:
                        throw new InvalidOperationException("elevated hosts write refused: payload changed on disk.");
                    case ExitBadPayload:
                        throw new InvalidOperationException("elevated hosts write refused: payload is not valid hosts entries.");
                    default:
                        throw new InvalidOperationException($"elevated hosts write failed (exit {proc.ExitCode}).");
                }
            }
        }
        finally
        {
            try { File.Delete(payloadPath); } catch { /* temp cleanup is best-effort */ }
        }
    }

    /// <summary>Belt-and-braces shape check: "address hostname [hostname…] [# comment]".</summary>
    internal static bool IsHostsEntry(string line)
    {
        var tokens = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length < 2) return false;
        if (!IPAddress.TryParse(tokens[0], out _)) return false;
        return !tokens[1].StartsWith('#');
    }
}

/// <summary>Hosts-file line helpers shared by <see cref="Providers.HostsProvider"/> and the elevation handoff.</summary>
internal static class HostsText
{
    public static IEnumerable<string> MeaningfulLines(IEnumerable<string> lines) =>
        lines.Select(l => l.Trim())
             .Where(l => l.Length > 0 && !l.StartsWith('#'));

    public static string Normalize(string line) =>
        string.Join(' ', line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
