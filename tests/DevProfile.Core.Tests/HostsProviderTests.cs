using DevProfile.Core;
using DevProfile.Core.Providers;
using Xunit;

namespace DevProfile.Core.Tests;

public sealed class HostsProviderTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("devprofile-tests-").FullName;
    private string HostsPath => Path.Combine(_dir, "hosts");
    private string ProfileDir => Path.Combine(_dir, "profile");

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private HostsProvider NewProvider() => new(HostsPath);

    private void WriteStored(params string[] lines)
    {
        Directory.CreateDirectory(Path.Combine(ProfileDir, "hosts"));
        File.WriteAllLines(Path.Combine(ProfileDir, "hosts", "hosts"), lines);
    }

    [Fact]
    public async Task Plan_AllEntriesPresent_IsSkip()
    {
        File.WriteAllLines(HostsPath, new[] { "127.0.0.1  myapp.local", "# comment" });
        WriteStored("# header comment", "127.0.0.1    myapp.local"); // same entry, different spacing

        var plan = await NewProvider().PlanAsync(ProfileDir);

        var item = Assert.Single(plan);
        Assert.Equal(PlanAction.Skip, item.Action);
    }

    [Fact]
    public async Task Plan_MissingEntries_IsMergeWithCount()
    {
        File.WriteAllLines(HostsPath, new[] { "127.0.0.1 existing.local" });
        WriteStored("127.0.0.1 existing.local", "127.0.0.1 new1.local", "10.0.0.5 new2.local");

        var plan = await NewProvider().PlanAsync(ProfileDir);

        var item = Assert.Single(plan);
        Assert.Equal(PlanAction.Merge, item.Action);
        Assert.StartsWith("2 missing", item.Status);
    }

    [Fact]
    public async Task Apply_AppendsOnlyMissing_AndBacksUp()
    {
        File.WriteAllLines(HostsPath, new[] { "127.0.0.1 existing.local" });
        WriteStored("127.0.0.1 existing.local", "127.0.0.1 new.local");
        var provider = NewProvider();
        var item = (await provider.PlanAsync(ProfileDir)).Single();

        await provider.ApplyAsync(ProfileDir, item, new ApplyOptions(), _ => { });

        var result = File.ReadAllLines(HostsPath);
        Assert.Contains("127.0.0.1 new.local", result);
        Assert.Single(result, l => l.Contains("existing.local")); // not duplicated
        Assert.True(File.Exists(HostsPath + ".devprofile.bak"));
    }

    [Fact]
    public async Task Apply_NothingMissing_LeavesFileUntouched()
    {
        File.WriteAllLines(HostsPath, new[] { "127.0.0.1 existing.local" });
        WriteStored("127.0.0.1   existing.local");
        var provider = NewProvider();
        var before = File.ReadAllText(HostsPath);

        await provider.ApplyAsync(
            ProfileDir,
            new PlanItem("hosts", "hosts entries", "forced", PlanAction.Merge),
            new ApplyOptions(), _ => { });

        Assert.Equal(before, File.ReadAllText(HostsPath));
        Assert.False(File.Exists(HostsPath + ".devprofile.bak"));
    }
}
