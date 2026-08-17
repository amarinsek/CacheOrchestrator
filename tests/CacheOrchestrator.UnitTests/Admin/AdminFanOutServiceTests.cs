using CacheOrchestrator.Admin;
using CacheOrchestrator.AdminConsole.Models;
using CacheOrchestrator.AdminConsole.Options;
using CacheOrchestrator.AdminConsole.Services;
using CacheOrchestrator.Invalidation;
using Microsoft.Extensions.Options;

namespace CacheOrchestrator.UnitTests.Admin;

public class AdminFanOutServiceTests
{
    [Fact]
    public void ResolveTarget_All_ReturnsEveryConfiguredInstance()
    {
        AdminFanOutService sut = CreateSut(
            new AdminInstanceOptions { Id = "a", Url = "http://a" },
            new AdminInstanceOptions { Id = "b", Url = "http://b" });

        IReadOnlyList<AdminInstanceOptions> targets = sut.ResolveTarget("all");
        targets.Select(t => t.Id).Should().BeEquivalentTo(["a", "b"]);
    }

    [Fact]
    public void ResolveTarget_InstancePrefix_ReturnsSingle()
    {
        AdminFanOutService sut = CreateSut(
            new AdminInstanceOptions { Id = "a", Url = "http://a" },
            new AdminInstanceOptions { Id = "b", Url = "http://b" });

        IReadOnlyList<AdminInstanceOptions> targets = sut.ResolveTarget("instance:b");
        targets.Should().ContainSingle(t => t.Id == "b");
    }

    [Fact]
    public void ResolveTarget_UnknownInstance_Throws()
    {
        AdminFanOutService sut = CreateSut(
            new AdminInstanceOptions { Id = "a", Url = "http://a" });

        Action act = () => sut.ResolveTarget("instance:missing");
        act.Should().Throw<KeyNotFoundException>();
    }

    [Fact]
    public async Task GetStatsAsync_ReturnsEmptyShell_PromOnlyConsole()
    {
        FakeLocalAdminClient client = new();
        AdminFanOutService sut = CreateSut(client,
            new AdminInstanceOptions { Id = "a", Url = "http://a" },
            new AdminInstanceOptions { Id = "b", Url = "http://b" });

#pragma warning disable CS0618
        ClusterStatsDto stats = await sut.GetStatsAsync("all", TestContext.Current.CancellationToken);
#pragma warning restore CS0618

        stats.Domains.Should().BeEmpty();
        stats.Endpoints.Should().BeEmpty();
        stats.Instances.Should().BeEmpty();
        client.StatsCallCountById.Should().BeEmpty("Console no longer fans out instance /stats");
    }

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
                Target = "all"
            },
            TestContext.Current.CancellationToken);

        result.DistributionMode.Should().Be(DistributionModes.FanOut);
        result.Distribute.Should().BeFalse();
        result.Results.Should().HaveCount(2);
        client.InvalidateCalls.Should().BeEquivalentTo(["a", "b"]);
        client.LastInvalidateBody!.Distribute.Should().BeFalse();
    }

    [Fact]
    public async Task InvalidateAsync_WhenBusAvailableAndTargetAll_UsesSingleOriginDistribute()
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
                Target = "all"
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
    public async Task InvalidateAsync_ExplicitInstanceWithoutBus_FansOutLocalOnly()
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
                Target = "instance:a"
            },
            TestContext.Current.CancellationToken);

        result.DistributionMode.Should().Be(DistributionModes.FanOut);
        result.Distribute.Should().BeFalse();
        result.Results.Should().ContainSingle(r => r.InstanceId == "a" && r.Succeeded);
        client.InvalidateCalls.Should().ContainSingle(c => c == "a");
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

    private static AdminFanOutService CreateSut(params AdminInstanceOptions[] instances) =>
        CreateSut(new FakeLocalAdminClient(), instances);

    private static AdminFanOutService CreateSut(ILocalAdminClient client, params AdminInstanceOptions[] instances)
    {
        AdminConsoleOptions opts = new()
        {
            Instances = instances.ToList(),
            Parallelism = 4,
            RequestTimeoutMs = 1000,
            DownReprobeSeconds = 15
        };
        Microsoft.Extensions.Options.IOptions<AdminConsoleOptions> options = Options.Create(opts);
        InstanceReachabilityCache reachability = new(options, TimeProvider.System);
        return new AdminFanOutService(client, options, reachability);
    }

    private sealed class FakeLocalAdminClient : ILocalAdminClient
    {
        public Dictionary<string, AdminLiveStatsRawSnapshot> Stats { get; } = new(StringComparer.Ordinal);
        public HashSet<string> FailStats { get; } = new(StringComparer.Ordinal);
        public HashSet<string> FailHealth { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, int> StatsCallCountById { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, int> HealthCallCountById { get; } = new(StringComparer.Ordinal);
        public List<string> InvalidateCalls { get; } = [];
        public Dictionary<string, LocalClusterInfoDto> ClusterInfo { get; } = new(StringComparer.Ordinal);
        public AdminInvalidateRequest? LastInvalidateBody { get; private set; }

        public Task<InstanceCallOutcome<AdminHealthDto>> GetHealthAsync(
            AdminInstanceOptions instance,
            CancellationToken cancellationToken = default)
        {
            HealthCallCountById[instance.Id] = HealthCallCountById.GetValueOrDefault(instance.Id) + 1;
            if (FailHealth.Contains(instance.Id) || FailStats.Contains(instance.Id))
                return Task.FromResult(Fail<AdminHealthDto>(instance.Id, "down"));

            return Task.FromResult(Ok(instance.Id, new AdminHealthDto
            {
                Healthy = true,
                InstanceId = instance.Id,
                UtcNow = DateTimeOffset.UtcNow,
                AdminEnabled = true
            }));
        }

#pragma warning disable CS0618
        public Task<InstanceCallOutcome<AdminLiveStatsRawSnapshot>> GetStatsAsync(
            AdminInstanceOptions instance,
            CancellationToken cancellationToken = default)
        {
            StatsCallCountById[instance.Id] = StatsCallCountById.GetValueOrDefault(instance.Id) + 1;
            if (FailStats.Contains(instance.Id))
                return Task.FromResult(Fail<AdminLiveStatsRawSnapshot>(instance.Id, "down"));

            if (!Stats.TryGetValue(instance.Id, out AdminLiveStatsRawSnapshot? snap))
                snap = new AdminLiveStatsRawSnapshot
                {
                    InstanceId = instance.Id,
                    CollectedAtUtc = DateTimeOffset.UtcNow,
                    Domains = [],
                    UnassignedEndpoints = [],
                    Endpoints = []
                };

            return Task.FromResult(Ok(instance.Id, snap));
        }
#pragma warning restore CS0618

        public Task<InstanceCallOutcome<IReadOnlyList<AdminEndpointInfoDto>>> GetEndpointsAsync(
            AdminInstanceOptions instance,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Ok(instance.Id, (IReadOnlyList<AdminEndpointInfoDto>)[]));

        public Task<InstanceCallOutcome<IReadOnlyList<AdminDomainConfigDto>>> GetDomainsAsync(
            AdminInstanceOptions instance,
            CancellationToken cancellationToken = default)
        {
            if (FailStats.Contains(instance.Id))
                return Task.FromResult(Fail<IReadOnlyList<AdminDomainConfigDto>>(instance.Id, "down"));
            return Task.FromResult(Ok(instance.Id, (IReadOnlyList<AdminDomainConfigDto>)[]));
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
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<InstanceCallOutcome<AdminDomainMutationResultDto>> PatchTtlAsync(
            AdminInstanceOptions instance,
            string domain,
            AdminTtlPatchRequest body,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

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
