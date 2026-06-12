using System.Security.Cryptography;
using System.Text;
using DevProfile.Core;
using Xunit;

namespace DevProfile.Core.Tests;

public sealed class HostsElevationTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("devprofile-hosts-elev-").FullName;
    private string HostsPath => Path.Combine(_dir, "hosts");

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string WritePayload(string json)
    {
        var path = Path.Combine(_dir, "payload.json");
        File.WriteAllText(path, json);
        return path;
    }

    private static string HashOf(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    [Fact]
    public void RunElevated_AppendsLines_AndBacksUp()
    {
        File.WriteAllLines(HostsPath, new[] { "127.0.0.1 localhost" });
        var payload = WritePayload("""{ "Lines": ["10.0.0.5 build-server", "10.0.0.6 staging"], "Backup": true }""");

        var exit = HostsElevation.RunElevated(payload, HashOf(payload), HostsPath);

        Assert.Equal(0, exit);
        var text = File.ReadAllText(HostsPath);
        Assert.Contains("10.0.0.5 build-server", text);
        Assert.Contains("10.0.0.6 staging", text);
        Assert.Contains("# Added by DevProfile", text);
        Assert.True(File.Exists(HostsPath + ".devprofile.bak"));
        // The backup holds the pre-append content.
        Assert.DoesNotContain("build-server", File.ReadAllText(HostsPath + ".devprofile.bak"));
    }

    [Fact]
    public void RunElevated_HashMismatch_RefusesWithoutWriting()
    {
        File.WriteAllLines(HostsPath, new[] { "127.0.0.1 localhost" });
        var payload = WritePayload("""{ "Lines": ["10.0.0.5 build-server"], "Backup": true }""");
        var staleHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("something else")));

        var exit = HostsElevation.RunElevated(payload, staleHash, HostsPath);

        Assert.Equal(4, exit);
        Assert.DoesNotContain("build-server", File.ReadAllText(HostsPath));
    }

    [Fact]
    public void RunElevated_NonHostsLines_RefusesWholePayload()
    {
        File.WriteAllLines(HostsPath, new[] { "127.0.0.1 localhost" });
        var payload = WritePayload("""{ "Lines": ["10.0.0.5 ok-entry", "not a hosts line at all"], "Backup": true }""");

        var exit = HostsElevation.RunElevated(payload, HashOf(payload), HostsPath);

        Assert.Equal(2, exit);
        Assert.DoesNotContain("ok-entry", File.ReadAllText(HostsPath));
    }

    [Fact]
    public void RunElevated_DedupesAgainstLiveFile()
    {
        File.WriteAllLines(HostsPath, new[] { "127.0.0.1 localhost", "10.0.0.5   build-server" });
        var payload = WritePayload("""{ "Lines": ["10.0.0.5 build-server", "10.0.0.6 staging"], "Backup": false }""");

        var exit = HostsElevation.RunElevated(payload, HashOf(payload), HostsPath);

        Assert.Equal(0, exit);
        var lines = File.ReadAllLines(HostsPath);
        Assert.Single(lines, l => l.Contains("build-server"));
        Assert.Contains(lines, l => l.Contains("staging"));
    }

    [Fact]
    public void RunElevated_MissingPayload_Refuses()
    {
        var exit = HostsElevation.RunElevated(Path.Combine(_dir, "nope.json"), "ABCD", HostsPath);
        Assert.Equal(2, exit);
    }

    [Theory]
    [InlineData("10.0.0.5 build-server", true)]
    [InlineData("::1 ip6-localhost", true)]
    [InlineData("10.0.0.5 host # trailing comment", true)]
    [InlineData("10.0.0.5", false)]            // no hostname
    [InlineData("not-an-ip host", false)]
    [InlineData("10.0.0.5 # only-comment", false)]
    [InlineData("", false)]
    public void IsHostsEntry_ChecksShape(string line, bool expected) =>
        Assert.Equal(expected, HostsElevation.IsHostsEntry(line));
}
