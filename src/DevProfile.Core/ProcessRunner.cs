using System.Diagnostics;
using System.Text;

namespace DevProfile.Core;

public sealed record ProcessResult(int ExitCode, string StdOut, string StdErr)
{
    public bool Ok => ExitCode == 0;
}

/// <summary>Thin wrapper for shelling out to CLIs (winget, git, code, npm, dotnet, pwsh).</summary>
public static class ProcessRunner
{
    /// <summary>Generous ceiling so a hung CLI can never freeze an apply run forever.</summary>
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(15);

    public static Task<ProcessResult> RunAsync(
        string fileName,
        string arguments,
        CancellationToken ct = default,
        string? workingDir = null,
        TimeSpan? timeout = null)
    {
        var psi = NewStartInfo(fileName, workingDir);
        psi.Arguments = arguments;
        return RunAsync(psi, ct, timeout);
    }

    /// <summary>
    /// Argument-list overload: each argument is escaped by the runtime, so values that came
    /// from an untrusted profile bundle cannot smuggle extra arguments or shell operators.
    /// </summary>
    public static Task<ProcessResult> RunAsync(
        string fileName,
        IEnumerable<string> arguments,
        CancellationToken ct = default,
        string? workingDir = null,
        TimeSpan? timeout = null)
    {
        var psi = NewStartInfo(fileName, workingDir);
        foreach (var a in arguments) psi.ArgumentList.Add(a);
        return RunAsync(psi, ct, timeout);
    }

    private static ProcessStartInfo NewStartInfo(string fileName, string? workingDir) => new()
    {
        FileName = fileName,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true,
        StandardOutputEncoding = Encoding.UTF8,
        StandardErrorEncoding = Encoding.UTF8,
        WorkingDirectory = workingDir ?? "",
    };

    private static async Task<ProcessResult> RunAsync(ProcessStartInfo psi, CancellationToken ct, TimeSpan? timeout)
    {
        using var proc = new Process { StartInfo = psi };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        proc.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
        proc.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };

        try
        {
            proc.Start();
        }
        catch (Exception ex)
        {
            return new ProcessResult(-1, "", ex.Message);
        }

        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(timeout ?? DefaultTimeout);
        try
        {
            await proc.WaitForExitAsync(limit.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // User cancel or timeout: take the whole child tree down so a winget/npm
            // install doesn't keep running invisibly after the UI says "cancelled".
            try { proc.Kill(entireProcessTree: true); } catch { /* already exited */ }
            if (ct.IsCancellationRequested) throw;
            return new ProcessResult(-1, stdout.ToString(),
                $"timed out after {(timeout ?? DefaultTimeout).TotalMinutes:0} min");
        }
        return new ProcessResult(proc.ExitCode, stdout.ToString(), stderr.ToString());
    }

    /// <summary>
    /// Run a command line through cmd.exe /c — needed for .cmd shims like npm and code.
    /// Only ever pass fixed, fully-trusted command lines here; anything that includes data
    /// from a profile bundle must be validated first (see <see cref="LabelValidation"/>).
    /// </summary>
    public static Task<ProcessResult> RunCmdAsync(string commandLine, CancellationToken ct = default) =>
        RunAsync("cmd.exe", $"/c {commandLine}", ct);

    /// <summary>True if a command resolves on PATH (via `where`).</summary>
    public static async Task<bool> ExistsAsync(string command, CancellationToken ct = default)
    {
        var r = await RunAsync("where", command, ct, timeout: TimeSpan.FromSeconds(30)).ConfigureAwait(false);
        return r.Ok && !string.IsNullOrWhiteSpace(r.StdOut);
    }
}
