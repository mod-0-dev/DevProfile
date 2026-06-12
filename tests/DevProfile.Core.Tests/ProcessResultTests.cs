using DevProfile.Core;
using Xunit;

namespace DevProfile.Core.Tests;

public class ProcessResultTests
{
    [Fact]
    public void ShortError_SurfacesLaunchFailureReason()
    {
        // What ProcessRunner returns when the executable can't be started at all.
        var r = new ProcessResult(-1, "", "The system cannot find the file specified.");
        Assert.Equal("exit -1: The system cannot find the file specified.", r.ShortError());
    }

    [Fact]
    public void ShortError_CollapsesMultiLineStderr_CmdNotRecognized()
    {
        // What `cmd /c npm …` writes when npm isn't on PATH — the headline is the FIRST line,
        // so the whole (short) stderr is kept rather than just the unhelpful second line.
        var r = new ProcessResult(1,
            "",
            "'npm' is not recognized as an internal or external command,\noperable program or batch file.");
        Assert.Equal(
            "exit 1: 'npm' is not recognized as an internal or external command, operable program or batch file.",
            r.ShortError());
    }

    [Fact]
    public void ShortError_FallsBackToStdout_WhenStderrEmpty()
    {
        // winget writes "No package found…" to stdout, not stderr.
        var r = new ProcessResult(-1978335212, "Found nothing\nNo package found matching input criteria.", "");
        Assert.Equal("exit -1978335212: No package found matching input criteria.", r.ShortError());
    }

    [Fact]
    public void ShortError_NoOutput_ReturnsCodeOnly()
    {
        var r = new ProcessResult(5, "", "   ");
        Assert.Equal("exit 5", r.ShortError());
    }

    [Fact]
    public void ShortError_CapsRunawayOutput()
    {
        var r = new ProcessResult(1, "", new string('x', 5000));
        var s = r.ShortError(maxLength: 200);
        Assert.True(s.Length < 230, $"expected truncated, got {s.Length} chars");
        Assert.EndsWith("…", s);
    }
}
