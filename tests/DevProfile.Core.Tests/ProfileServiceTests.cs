using DevProfile.Core;
using Xunit;

namespace DevProfile.Core.Tests;

/// <summary>Scriptable in-memory provider for orchestration tests.</summary>
file sealed class FakeProvider : IProvider
{
    public FakeProvider(string id) => Id = id;

    public string Id { get; }
    public string DisplayName => Id;
    public ProviderCategory Category => ProviderCategory.Packages;
    public bool ContainsSecrets => false;

    public bool CaptureThrows { get; init; }
    public IReadOnlyList<PlanItem> PlanItems { get; init; } = Array.Empty<PlanItem>();

    public List<string> AppliedLabels { get; } = new();
    public bool Captured { get; private set; }

    public Task<DiscoveryResult> DiscoverAsync(CancellationToken ct = default) =>
        Task.FromResult(DiscoveryResult.Found("ok"));

    public Task CaptureAsync(string profileDir, ExportOptions options, CancellationToken ct = default)
    {
        if (CaptureThrows) throw new InvalidOperationException("capture boom");
        Captured = true;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<PlanItem>> PlanAsync(string profileDir, CancellationToken ct = default) =>
        Task.FromResult(PlanItems);

    public Task ApplyAsync(string profileDir, PlanItem item, ApplyOptions options, Action<string> log, CancellationToken ct = default)
    {
        AppliedLabels.Add(item.Label);
        log($"applied:{Id}");
        return Task.CompletedTask;
    }
}

public sealed class ProfileServiceTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("devprofile-tests-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public async Task Export_WritesManifest_WithOnlySuccessfulProviders()
    {
        var ok = new FakeProvider("ok");
        var broken = new FakeProvider("broken") { CaptureThrows = true };
        var unselected = new FakeProvider("unselected");
        var service = new ProfileService(new[] { ok, broken, unselected });

        await service.ExportAsync(_dir, new[] { "ok", "broken" }, new ExportOptions(), _ => { });

        Assert.True(ok.Captured);
        Assert.False(unselected.Captured);
        var manifest = await service.ReadManifestAsync(_dir);
        Assert.NotNull(manifest);
        Assert.Equal(ProfileService.SupportedSchema, manifest!.Schema);
        Assert.Equal(new[] { "ok" }, manifest.Providers);
    }

    [Fact]
    public async Task BuildPlan_WithoutManifest_Throws()
    {
        var service = new ProfileService(new[] { new FakeProvider("ok") });
        await Assert.ThrowsAsync<InvalidDataException>(() => service.BuildPlanAsync(_dir));
    }

    [Fact]
    public async Task ReadManifest_UnsupportedSchema_Throws()
    {
        File.WriteAllText(Path.Combine(_dir, "profile.json"), """{ "Schema": "devprofile/v99" }""");
        var service = new ProfileService(new[] { new FakeProvider("ok") });
        await Assert.ThrowsAsync<InvalidDataException>(() => service.ReadManifestAsync(_dir));
    }

    [Fact]
    public async Task BuildPlan_OnlyAsksProvidersListedInManifest()
    {
        var inManifest = new FakeProvider("in")
        {
            PlanItems = new[] { new PlanItem("in", "thing", "missing", PlanAction.Install) },
        };
        var notInManifest = new FakeProvider("out")
        {
            PlanItems = new[] { new PlanItem("out", "other", "missing", PlanAction.Install) },
        };
        var service = new ProfileService(new[] { inManifest, notInManifest });
        await service.ExportAsync(_dir, new[] { "in" }, new ExportOptions(), _ => { });

        var plan = await service.BuildPlanAsync(_dir);

        var item = Assert.Single(plan);
        Assert.Equal("in", item.ProviderId);
    }

    [Fact]
    public async Task Apply_RunsWingetPhaseFirst()
    {
        var winget = new FakeProvider("winget");
        var npm = new FakeProvider("npm-global");
        var service = new ProfileService(new IProvider[] { npm, winget });
        var order = new List<string>();
        var items = new[]
        {
            new PlanItem("npm-global", "typescript", "missing", PlanAction.Install),
            new PlanItem("winget", "Git.Git", "missing", PlanAction.Install),
        };

        await service.ApplyAsync(_dir, items, new ApplyOptions(), order.Add);

        // winget runs first despite input order, then PATH refresh, then the npm item.
        Assert.Equal(new[] { "Git.Git" }, winget.AppliedLabels);
        Assert.Equal(new[] { "typescript" }, npm.AppliedLabels);
        int iWinget = order.FindIndex(l => l == "applied:winget");
        int iRefresh = order.FindIndex(l => l.Contains("Refreshed PATH"));
        int iNpm = order.FindIndex(l => l == "applied:npm-global");
        Assert.True(0 <= iWinget && iWinget < iRefresh && iRefresh < iNpm);
    }

    [Fact]
    public async Task Apply_ProviderFailure_IsLoggedNotThrown()
    {
        var service = new ProfileService(new[] { new FakeProvider("ok") });
        var log = new List<string>();
        var items = new[] { new PlanItem("missing-provider", "x", "missing", PlanAction.Install) };

        // Unknown provider id is simply skipped; no exception escapes.
        await service.ApplyAsync(_dir, items, new ApplyOptions(), log.Add);

        Assert.Contains(log, l => l.Contains("Apply complete"));
    }
}
