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

    /// <summary>Non-null makes preflight fail, so the orchestrator should skip every item.</summary>
    public string? PreflightReason { get; init; }

    public List<string> AppliedLabels { get; } = new();
    public bool Captured { get; private set; }

    public Task<DiscoveryResult> DiscoverAsync(CancellationToken ct = default) =>
        Task.FromResult(DiscoveryResult.Found("ok"));

    public Task<string?> PreflightAsync(CancellationToken ct = default) =>
        Task.FromResult(PreflightReason);

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
    public async Task Apply_RestoresSecretsBeforePackageConsumers()
    {
        // secrets writes ~/.npmrc; npm globals may need a token from it, so secrets must
        // run first even though it sits last in the provider registry / input order.
        var npm = new FakeProvider("npm-global");
        var secrets = new FakeProvider("secrets");
        var service = new ProfileService(new IProvider[] { npm, secrets });
        var order = new List<string>();
        var items = new[]
        {
            new PlanItem("npm-global", "typescript", "missing", PlanAction.Install),
            new PlanItem("secrets", "SSH keys & tokens", "encrypted", PlanAction.Install),
        };

        await service.ApplyAsync(_dir, items, new ApplyOptions(), order.Add);

        int iSecrets = order.FindIndex(l => l == "applied:secrets");
        int iNpm = order.FindIndex(l => l == "applied:npm-global");
        Assert.True(0 <= iSecrets && iSecrets < iNpm, $"secrets({iSecrets}) should precede npm({iNpm})");
    }

    [Fact]
    public async Task Apply_FailedPreflight_SkipsAllItemsForThatProvider_WithOneMessage()
    {
        var npm = new FakeProvider("npm-global") { PreflightReason = "npm isn't on PATH — install Node.js." };
        var git = new FakeProvider("git-config"); // preflight passes (null) -> applies normally
        var service = new ProfileService(new IProvider[] { npm, git });
        var log = new List<string>();
        var items = new[]
        {
            new PlanItem("npm-global", "typescript", "missing", PlanAction.Install),
            new PlanItem("npm-global", "eslint", "missing", PlanAction.Install),
            new PlanItem("git-config", ".gitconfig", "differs", PlanAction.Overwrite),
        };

        await service.ApplyAsync(_dir, items, new ApplyOptions(), log.Add);

        // npm skipped wholesale, never applied; git unaffected.
        Assert.Empty(npm.AppliedLabels);
        Assert.Equal(new[] { ".gitconfig" }, git.AppliedLabels);

        // Exactly one skip line, naming the provider, the reason, and the count of items dropped.
        var skipLines = log.Where(l => l.Contains("Skipping npm-global")).ToList();
        Assert.Single(skipLines);
        Assert.Contains("install Node.js", skipLines[0]);
        Assert.Contains("2 item(s) not applied", skipLines[0]);
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
