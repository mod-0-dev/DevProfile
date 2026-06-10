using DevProfile.Core;
using DevProfile.Core.Providers;
using Xunit;

namespace DevProfile.Core.Tests;

public sealed class SingleFileProviderTests : IDisposable
{
    private sealed class TestFileProvider : SingleFileProvider
    {
        public TestFileProvider(string livePath) => LivePath = livePath;
        public override string Id => "test-file";
        public override string DisplayName => "test file";
        public override ProviderCategory Category => ProviderCategory.Shell;
        protected override string LivePath { get; }
        protected override string StoredFileName => "config.txt";
    }

    private readonly string _dir = Directory.CreateTempSubdirectory("devprofile-tests-").FullName;
    private string LivePath => Path.Combine(_dir, "live", "config.txt");
    private string ProfileDir => Path.Combine(_dir, "profile");
    private string StoredPath => Path.Combine(ProfileDir, "test-file", "config.txt");

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private TestFileProvider NewProvider() => new(LivePath);

    private void WriteLive(string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(LivePath)!);
        File.WriteAllText(LivePath, content);
    }

    private void WriteStored(string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(StoredPath)!);
        File.WriteAllText(StoredPath, content);
    }

    [Fact]
    public async Task Capture_CopiesLiveFileIntoBundle()
    {
        WriteLive("hello");
        await NewProvider().CaptureAsync(ProfileDir, new ExportOptions());
        Assert.Equal("hello", File.ReadAllText(StoredPath));
    }

    [Fact]
    public async Task Plan_NothingStored_IsEmpty() =>
        Assert.Empty(await NewProvider().PlanAsync(ProfileDir));

    [Fact]
    public async Task Plan_StoredButNoLive_IsInstall()
    {
        WriteStored("hello");
        var item = Assert.Single(await NewProvider().PlanAsync(ProfileDir));
        Assert.Equal(PlanAction.Install, item.Action);
    }

    [Fact]
    public async Task Plan_Identical_IsSkip()
    {
        WriteStored("same");
        WriteLive("same");
        var item = Assert.Single(await NewProvider().PlanAsync(ProfileDir));
        Assert.Equal(PlanAction.Skip, item.Action);
    }

    [Fact]
    public async Task Plan_Different_IsOverwrite()
    {
        WriteStored("new");
        WriteLive("old");
        var item = Assert.Single(await NewProvider().PlanAsync(ProfileDir));
        Assert.Equal(PlanAction.Overwrite, item.Action);
    }

    [Fact]
    public async Task Apply_Overwrite_BacksUpThenWrites()
    {
        WriteStored("new");
        WriteLive("old");
        var provider = NewProvider();
        var item = (await provider.PlanAsync(ProfileDir)).Single();

        await provider.ApplyAsync(ProfileDir, item, new ApplyOptions(), _ => { });

        Assert.Equal("new", File.ReadAllText(LivePath));
        Assert.Equal("old", File.ReadAllText(LivePath + ".devprofile.bak"));
    }

    [Fact]
    public async Task Apply_Overwrite_WithoutBackupOption_TakesNoBackup()
    {
        WriteStored("new");
        WriteLive("old");
        var provider = NewProvider();
        var item = (await provider.PlanAsync(ProfileDir)).Single();

        await provider.ApplyAsync(ProfileDir, item, new ApplyOptions(BackupOnOverwrite: false), _ => { });

        Assert.Equal("new", File.ReadAllText(LivePath));
        Assert.False(File.Exists(LivePath + ".devprofile.bak"));
    }

    [Fact]
    public async Task Apply_Skip_DoesNothing()
    {
        WriteStored("same");
        WriteLive("same");
        var provider = NewProvider();
        var item = (await provider.PlanAsync(ProfileDir)).Single();

        await provider.ApplyAsync(ProfileDir, item, new ApplyOptions(), _ => { });

        Assert.False(File.Exists(LivePath + ".devprofile.bak"));
    }

    [Fact]
    public async Task Apply_Install_CreatesMissingDirectory()
    {
        WriteStored("fresh");
        var provider = NewProvider();
        var item = (await provider.PlanAsync(ProfileDir)).Single();

        await provider.ApplyAsync(ProfileDir, item, new ApplyOptions(), _ => { });

        Assert.Equal("fresh", File.ReadAllText(LivePath));
    }
}
