using CacheOrchestrator.Admin;
using CacheOrchestrator.AdminConsole.Models;
using CacheOrchestrator.AdminConsole.Options;
using CacheOrchestrator.AdminConsole.Services;
using CacheOrchestrator.Invalidation;
using Microsoft.Extensions.Options;

namespace CacheOrchestrator.AdminConsole.UnitTests;

public class AdminFanOutServiceTests
{
    [Fact]
    public async Task GetOverviewAsync_HealthOnly_NoTrafficCounters()
    {
        FakeLocalAdminClient client = new();
        AdminFanOutService sut = CreateSut(client,
            new AdminInstanceOptions { Id = "a", Url = "http://a" },
            new AdminInstanceOptions { Id = "b", Url = "http://b" });

        OverviewDto overview = await sut.GetOverviewAsync(TestContext.Current.CancellationToken);

        overview.Instances.Should().HaveCount(2);
        overview.HealthyCount.Should().Be(2);
        overview.TopDomains.Should().BeEmpty();
        overview.TopEndpoints.Should().BeEmpty();
        overview.TotalRequests.Should().Be(0);
        overview.StatsWindow.Should().Be("metrics-store");
    }


    [Fact]
    public async Task GetInstancesAsync_SkipsKnownDownInstance_OnSecondCall()
    {
        FakeLocalAdminClient client = new();
        client.FailHealth.Add("b");

        AdminFanOutService sut = CreateSut(client,
            new AdminInstanceOptions { Id = "a", Url = "http://a" },
            new AdminInstanceOptions { Id = "b", Url = "http://b" });

        await sut.GetInstancesAsync(TestContext.Current.CancellationToken);
        int afterFirst = client.HealthCallCountById.GetValueOrDefault("b");
        afterFirst.Should().Be(1);

        await sut.GetInstancesAsync(TestContext.Current.CancellationToken);
        client.HealthCallCountById.GetValueOrDefault("b").Should().Be(1, "down instance is skipped until re-probe");
        client.HealthCallCountById.GetValueOrDefault("a").Should().Be(2);
    }

    [Fact]
    public async Task GetInstancesAsync_ReprobesDownInstance_AfterDownReprobeSeconds()
    {
        FakeLocalAdminClient client = new();
        client.FailHealth.Add("b");
        TestMutableTimeProvider time = new(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));

        AdminFanOutService sut = CreateSut(
            client,
            time,
            downReprobeSeconds: 15,
            new AdminInstanceOptions { Id = "a", Url = "http://a" },
            new AdminInstanceOptions { Id = "b", Url = "http://b" });

        await sut.GetInstancesAsync(TestContext.Current.CancellationToken);
        client.HealthCallCountById.GetValueOrDefault("b").Should().Be(1);

        await sut.GetInstancesAsync(TestContext.Current.CancellationToken);
        client.HealthCallCountById.GetValueOrDefault("b").Should().Be(1);

        time.Advance(TimeSpan.FromSeconds(16));
        await sut.GetInstancesAsync(TestContext.Current.CancellationToken);
        client.HealthCallCountById.GetValueOrDefault("b").Should().Be(2, "re-probe after DownReprobeSeconds");
    }

    [Fact]
    public async Task InvalidateAsync_WhenNoBus_FansOutWithDistributeFalse()
    {
        FakeLocalAdminClient client = new();
        AdminFanOutService sut = CreateSut(client,
            new AdminInstanceOptions { Id = "a", Url = "http://a" },
            new AdminInstanceOptions { Id = "b", Url = "http://b" });

        FanOutResultDto<object?> result = await sut.InvalidateAsync(
            new AdminConsoleInvalidateRequest
            {
                Scope = "domain",
                Domain = "catalog",
            },
            TestContext.Current.CancellationToken);

        result.DistributionMode.Should().Be(DistributionModes.FanOut);
        result.Distribute.Should().BeFalse();
        result.Outcome.Should().Be(WriteOutcomes.Success);
        result.FailedInstanceIds.Should().BeEmpty();
        result.Results.Should().HaveCount(2);
        client.InvalidateCalls.Should().BeEquivalentTo(["a", "b"]);
        client.LastInvalidateBody!.Distribute.Should().BeFalse();
    }

    [Fact]
    public void ExpandWriteResults_ClusterPublishIncomplete_MarksOriginAppliedAndPeersFailed()
    {
        InstanceCallOutcome<object?> origin = new()
        {
            InstanceId = "playground-a",
            Succeeded = false,
            StatusCode = 409,
            Error = "Cluster publish incomplete.",
            LatencyMs = 2000,
            LocalApplied = true,
            PeerFailures =
            [
                new LocalAdminPeerFailureDto
                {
                    PeerId = "playground-b",
                    Error = "Timed out after 2000ms",
                },
            ],
        };

        IReadOnlyList<InstanceCallResultDto> results = AdminFanOutService.ExpandWriteResults([origin]);
        FanOutResultDto<object?> aggregate = new FanOutResultDto<object?>
        {
            Results = results,
            DistributionMode = DistributionModes.BusDistribute,
            Distribute = true,
            BusOriginInstanceId = "playground-a",
        }.WithWriteOutcome();

        results.Should().HaveCount(2);
        results.Should().ContainSingle(r => r.InstanceId == "playground-a" && r.Succeeded);
        results.Should().ContainSingle(r =>
            r.InstanceId == "playground-b" && !r.Succeeded && r.Error!.Contains("Timed out"));
        aggregate.Outcome.Should().Be(WriteOutcomes.PartialFailure);
        aggregate.FailedInstanceIds.Should().Equal("playground-b");
    }

    [Fact]
    public async Task InvalidateAsync_WhenOneInstanceSkippedDown_PartialFailure()
    {
        FakeLocalAdminClient client = new();
        client.FailHealth.Add("b");
        AdminFanOutService sut = CreateSut(client,
            new AdminInstanceOptions { Id = "a", Url = "http://a" },
            new AdminInstanceOptions { Id = "b", Url = "http://b" });

        // Seed reachability so write fan-out skips b without HTTP.
        await sut.GetInstancesAsync(TestContext.Current.CancellationToken);

        FanOutResultDto<object?> result = await sut.InvalidateAsync(
            new AdminConsoleInvalidateRequest
            {
                Scope = "domain",
                Domain = "catalog",
            },
            TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(WriteOutcomes.PartialFailure);
        result.FailedInstanceIds.Should().ContainSingle(id => id == "b");
        result.Warning.Should().NotBeNullOrWhiteSpace();
        client.InvalidateCalls.Should().ContainSingle(c => c == "a");
    }

    [Fact]
    public async Task InvalidateAsync_WhenBusAvailable_UsesSingleOriginDistribute()
    {
        FakeLocalAdminClient client = new();
        client.ClusterInfo["a"] = new LocalClusterInfoDto
        {
            InstanceId = "a",
            BusEnabled = true,
            Membership = "Static",
            PeerCount = 2
        };
        client.ClusterInfo["b"] = new LocalClusterInfoDto
        {
            InstanceId = "b",
            BusEnabled = true,
            Membership = "Static",
            PeerCount = 2
        };

        AdminFanOutService sut = CreateSut(client,
            new AdminInstanceOptions { Id = "a", Url = "http://a" },
            new AdminInstanceOptions { Id = "b", Url = "http://b" });

        FanOutResultDto<object?> result = await sut.InvalidateAsync(
            new AdminConsoleInvalidateRequest
            {
                Scope = "domain",
                Domain = "catalog",
            },
            TestContext.Current.CancellationToken);

        result.DistributionMode.Should().Be(DistributionModes.BusDistribute);
        result.Distribute.Should().BeTrue();
        result.BusOriginInstanceId.Should().Be("a");
        result.Results.Should().ContainSingle(r => r.InstanceId == "a");
        client.InvalidateCalls.Should().ContainSingle(c => c == "a");
        client.LastInvalidateBody!.Distribute.Should().BeTrue();
    }

    [Fact]
    public async Task GetDistributionCapabilityAsync_ReportsBusPreferredOrigin()
    {
        FakeLocalAdminClient client = new();
        client.ClusterInfo["b"] = new LocalClusterInfoDto
        {
            InstanceId = "b",
            BusEnabled = true,
            Membership = "Static",
            PeerCount = 1
        };

        AdminFanOutService sut = CreateSut(client,
            new AdminInstanceOptions { Id = "a", Url = "http://a" },
            new AdminInstanceOptions { Id = "b", Url = "http://b" });

        ClusterDistributionCapabilityDto cap =
            await sut.GetDistributionCapabilityAsync(TestContext.Current.CancellationToken);

        cap.BusAvailable.Should().BeTrue();
        cap.RecommendedMode.Should().Be(DistributionModes.BusDistribute);
        cap.PreferredBusOriginId.Should().Be("b");
    }

    [Fact]
    public async Task GetDomainsAsync_MergesConfigsFromReachableInstances()
    {
        FakeLocalAdminClient client = new();
        client.DomainsById["a"] =
        [
            new AdminDomainConfigDto
            {
                Name = "catalog",
                Version = "1",
                FusionCacheInstanceName = "default",
            },
        ];
        client.DomainsById["b"] =
        [
            new AdminDomainConfigDto
            {
                Name = "catalog",
                Version = "1",
                FusionCacheInstanceName = "default",
            },
            new AdminDomainConfigDto
            {
                Name = "maps",
                Version = "2",
                FusionCacheInstanceName = "default",
            },
        ];

        AdminFanOutService sut = CreateSut(client,
            new AdminInstanceOptions { Id = "a", Url = "http://a" },
            new AdminInstanceOptions { Id = "b", Url = "http://b" });

        FanOutResultDto<IReadOnlyList<AdminDomainConfigDto>> result =
            await sut.GetDomainsAsync(TestContext.Current.CancellationToken);

        result.Results.Should().HaveCount(2);
        result.Results.Should().OnlyContain(r => r.Succeeded);
        result.Data.Should().NotBeNull();
        result.Data!.Select(d => d.Name).Should().BeEquivalentTo(["catalog", "maps"]);
    }

    [Fact]
    public async Task SetVersionAsync_FansOutWithDistributeFalse_WhenNoBus()
    {
        FakeLocalAdminClient client = new();
        AdminFanOutService sut = CreateSut(client,
            new AdminInstanceOptions { Id = "a", Url = "http://a" },
            new AdminInstanceOptions { Id = "b", Url = "http://b" });

        FanOutResultDto<object?> result = await sut.SetVersionAsync(
            "catalog",
            new AdminConsoleVersionRequest { Version = "bump" },
            TestContext.Current.CancellationToken);

        result.DistributionMode.Should().Be(DistributionModes.FanOut);
        result.Distribute.Should().BeFalse();
        client.VersionCalls.Should().BeEquivalentTo(["a:catalog", "b:catalog"]);
        client.LastVersionBody!.Version.Should().Be("bump");
        client.LastVersionBody.Distribute.Should().BeFalse();
    }

    [Fact]
    public async Task PatchTtlAsync_FansOutWithDistributeFalse_WhenNoBus()
    {
        FakeLocalAdminClient client = new();
        AdminFanOutService sut = CreateSut(client,
            new AdminInstanceOptions { Id = "a", Url = "http://a" },
            new AdminInstanceOptions { Id = "b", Url = "http://b" });

        FanOutResultDto<object?> result = await sut.PatchTtlAsync(
            "catalog",
            new AdminConsoleTtlPatchRequest
            {
                OutputCacheTtlSeconds = 120,
            },
            TestContext.Current.CancellationToken);

        result.DistributionMode.Should().Be(DistributionModes.FanOut);
        client.TtlCalls.Should().BeEquivalentTo(["a:catalog", "b:catalog"]);
        client.LastTtlBody!.OutputCacheTtlSeconds.Should().Be(120);
        client.LastTtlBody.Distribute.Should().BeFalse();
    }

    private static AdminFanOutService CreateSut(params AdminInstanceOptions[] instances) =>
        CreateSut(new FakeLocalAdminClient(), instances);

    private static AdminFanOutService CreateSut(ILocalAdminClient client, params AdminInstanceOptions[] instances) =>
        CreateSut(client, TimeProvider.System, downReprobeSeconds: 15, instances);

    private static AdminFanOutService CreateSut(
        ILocalAdminClient client,
        TimeProvider time,
        int downReprobeSeconds,
        params AdminInstanceOptions[] instances)
    {
        AdminConsoleOptions opts = new()
        {
            Instances = instances.ToList(),
            Parallelism = 4,
            RequestTimeoutMs = 1000,
            DownReprobeSeconds = downReprobeSeconds
        };
        Microsoft.Extensions.Options.IOptions<AdminConsoleOptions> options = Microsoft.Extensions.Options.Options.Create(opts);
        InstanceReachabilityCache reachability = new(options, time);
        return new AdminFanOutService(client, options, reachability, time);
    }

    private sealed class FakeLocalAdminClient : ILocalAdminClient
    {
        public HashSet<string> FailHealth { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, int> HealthCallCountById { get; } = new(StringComparer.Ordinal);
        public List<string> InvalidateCalls { get; } = [];
        public List<string> VersionCalls { get; } = [];
        public List<string> TtlCalls { get; } = [];
        public Dictionary<string, LocalClusterInfoDto> ClusterInfo { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, IReadOnlyList<AdminDomainConfigDto>> DomainsById { get; } =
            new(StringComparer.Ordinal);
        public AdminInvalidateRequest? LastInvalidateBody { get; private set; }
        public AdminVersionRequest? LastVersionBody { get; private set; }
        public AdminTtlPatchRequest? LastTtlBody { get; private set; }

        public Task<InstanceCallOutcome<AdminHealthDto>> GetHealthAsync(
            AdminInstanceOptions instance,
            CancellationToken cancellationToken = default)
        {
            HealthCallCountById[instance.Id] = HealthCallCountById.GetValueOrDefault(instance.Id) + 1;
            if (FailHealth.Contains(instance.Id))
                return Task.FromResult(Fail<AdminHealthDto>(instance.Id, "down"));

            return Task.FromResult(Ok(instance.Id, new AdminHealthDto
            {
                Healthy = true,
                InstanceId = instance.Id,
                UtcNow = DateTimeOffset.UtcNow,
                AdminEnabled = true
            }));
        }

        public Task<InstanceCallOutcome<IReadOnlyList<AdminDomainConfigDto>>> GetDomainsAsync(
            AdminInstanceOptions instance,
            CancellationToken cancellationToken = default)
        {
            if (FailHealth.Contains(instance.Id))
                return Task.FromResult(Fail<IReadOnlyList<AdminDomainConfigDto>>(instance.Id, "down"));
            if (!DomainsById.TryGetValue(instance.Id, out IReadOnlyList<AdminDomainConfigDto>? list))
                list = [];
            return Task.FromResult(Ok(instance.Id, list));
        }

        public Task<InstanceCallOutcome<CacheInvalidationResult>> InvalidateAsync(
            AdminInstanceOptions instance,
            AdminInvalidateRequest body,
            CancellationToken cancellationToken = default)
        {
            InvalidateCalls.Add(instance.Id);
            LastInvalidateBody = body;
            return Task.FromResult(Ok(instance.Id, CacheInvalidationResult.Skipped("test")));
        }

        public Task<InstanceCallOutcome<AdminDomainMutationResultDto>> SetVersionAsync(
            AdminInstanceOptions instance,
            string domain,
            AdminVersionRequest body,
            CancellationToken cancellationToken = default)
        {
            VersionCalls.Add(instance.Id + ":" + domain);
            LastVersionBody = body;
            return Task.FromResult(Ok(instance.Id, new AdminDomainMutationResultDto
            {
                Domain = domain,
                Effective = new AdminDomainConfigDto
                {
                    Name = domain,
                    Version = body.Version ?? "generated",
                    FusionCacheInstanceName = "default",
                },
            }));
        }

        public Task<InstanceCallOutcome<AdminDomainMutationResultDto>> PatchTtlAsync(
            AdminInstanceOptions instance,
            string domain,
            AdminTtlPatchRequest body,
            CancellationToken cancellationToken = default)
        {
            TtlCalls.Add(instance.Id + ":" + domain);
            LastTtlBody = body;
            return Task.FromResult(Ok(instance.Id, new AdminDomainMutationResultDto
            {
                Domain = domain,
                Effective = new AdminDomainConfigDto
                {
                    Name = domain,
                    Version = "1",
                    FusionCacheInstanceName = "default",
                    OutputCacheTtlSeconds = body.OutputCacheTtlSeconds ?? 0,
                },
            }));
        }

        public Task<InstanceCallOutcome<AdminDomainMutationResultDto>> PatchSettingsAsync(
            AdminInstanceOptions instance,
            string domain,
            AdminSettingsPatchRequest body,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Ok(instance.Id, new AdminDomainMutationResultDto
            {
                Domain = domain,
                Effective = new AdminDomainConfigDto
                {
                    Name = domain,
                    Version = "1",
                    FusionCacheInstanceName = "default",
                },
            }));

        public Task<InstanceCallOutcome<AdminDomainSettingsCatalogDto>> GetDomainSettingsCatalogAsync(
            AdminInstanceOptions instance,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Ok(instance.Id, new AdminDomainSettingsCatalogDto { Settings = [] }));

        public Task<InstanceCallOutcome<LocalClusterInfoDto>> GetClusterInfoAsync(
            AdminInstanceOptions instance,
            CancellationToken cancellationToken = default)
        {
            if (ClusterInfo.TryGetValue(instance.Id, out LocalClusterInfoDto? info))
                return Task.FromResult(Ok(instance.Id, info));

            // Default: no bus endpoints (simulates bus disabled / not mapped).
            return Task.FromResult(Fail<LocalClusterInfoDto>(instance.Id, "cluster info unavailable"));
        }

        private static InstanceCallOutcome<T> Ok<T>(string id, T value) =>
            new()
            {
                InstanceId = id,
                Succeeded = true,
                Value = value,
                StatusCode = 200,
                LatencyMs = 1
            };

        private static InstanceCallOutcome<T> Fail<T>(string id, string error) =>
            new()
            {
                InstanceId = id,
                Succeeded = false,
                Error = error,
                LatencyMs = 1
            };
    }
}
