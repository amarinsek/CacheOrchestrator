using CacheOrchestrator.Admin;
using CacheOrchestrator.Admin.App.Models;
using CacheOrchestrator.Admin.App.Options;
using CacheOrchestrator.Admin.App.Services;
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
    public async Task GetStatsAsync_AggregatesSuccessfulInstances_IgnoresFailures()
    {
        FakeLocalAdminClient client = new();
        (long req, AdminLayerDto oc, AdminFusionLayerDto fc, AdminPipelineDto pipe) =
            AdminStatsMath.BuildAll(10, 0, 0, 5, 5, 0, 0, 5, 0);
        client.Stats["a"] = new AdminLiveStatsSnapshot
        {
            InstanceId = "a",
            CollectedAtUtc = DateTimeOffset.UtcNow,
            Domains =
            [
                new AdminDomainStatsDto
                {
                    Name = "catalog",
                    Version = "1",
                    Requests = req,
                    Oc = oc,
                    Fc = fc,
                    Pipeline = pipe,
                    Endpoints = []
                }
            ],
            UnassignedEndpoints = [],
            Endpoints = []
        };
        client.FailStats.Add("b");

        AdminFanOutService sut = CreateSut(client,
            new AdminInstanceOptions { Id = "a", Url = "http://a" },
            new AdminInstanceOptions { Id = "b", Url = "http://b" });

        ClusterStatsDto stats = await sut.GetStatsAsync("all", TestContext.Current.CancellationToken);

        stats.Instances.Should().HaveCount(2);
        stats.Instances.Count(i => i.Succeeded).Should().Be(1);
        stats.Domains.Should().ContainSingle(d => d.Name == "catalog" && d.Oc.Hits == 10);
    }

    [Fact]
    public async Task GetStatsAsync_SkipsKnownDownInstance_OnSecondCall()
    {
        FakeLocalAdminClient client = new();
        client.FailStats.Add("b");
        client.Stats["a"] = new AdminLiveStatsSnapshot
        {
            InstanceId = "a",
            CollectedAtUtc = DateTimeOffset.UtcNow,
            Domains = [],
            UnassignedEndpoints = [],
            Endpoints = []
        };

        AdminFanOutService sut = CreateSut(client,
            new AdminInstanceOptions { Id = "a", Url = "http://a" },
            new AdminInstanceOptions { Id = "b", Url = "http://b" });

        await sut.GetStatsAsync("all", TestContext.Current.CancellationToken);
        int afterFirst = client.StatsCallCountById.GetValueOrDefault("b");
        afterFirst.Should().Be(1);

        await sut.GetStatsAsync("all", TestContext.Current.CancellationToken);
        client.StatsCallCountById.GetValueOrDefault("b").Should().Be(1, "down instance is skipped until re-probe");
        client.StatsCallCountById.GetValueOrDefault("a").Should().Be(2);
    }

    [Fact]
    public async Task InvalidateAsync_WhenNoBus_FansOutWithDistributeFalse()
    {
        FakeLocalAdminClient client = new();
        AdminFanOutService sut = CreateSut(client,
            new AdminInstanceOptions { Id = "a", Url = "http://a" },
            new AdminInstanceOptions { Id = "b", Url = "http://b" });

        FanOutResultDto<object?> result = await sut.InvalidateAsync(
            new AdminAppInvalidateRequest
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
            new AdminAppInvalidateRequest
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
            new AdminAppInvalidateRequest
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
        CacheAdminOptions opts = new()
        {
            Instances = instances.ToList(),
            Parallelism = 4,
            RequestTimeoutMs = 1000,
            DownReprobeSeconds = 15
        };
        Microsoft.Extensions.Options.IOptions<CacheAdminOptions> options = Options.Create(opts);
        InstanceReachabilityCache reachability = new(options, TimeProvider.System);
        return new AdminFanOutService(client, options, reachability);
    }

    private sealed class FakeLocalAdminClient : ILocalAdminClient
    {
        public Dictionary<string, AdminLiveStatsSnapshot> Stats { get; } = new(StringComparer.Ordinal);
        public HashSet<string> FailStats { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, int> StatsCallCountById { get; } = new(StringComparer.Ordinal);
        public List<string> InvalidateCalls { get; } = [];
        public Dictionary<string, LocalClusterInfoDto> ClusterInfo { get; } = new(StringComparer.Ordinal);
        public AdminInvalidateRequest? LastInvalidateBody { get; private set; }

        public Task<InstanceCallOutcome<AdminHealthDto>> GetHealthAsync(
            AdminInstanceOptions instance,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Ok(instance.Id, new AdminHealthDto
            {
                Healthy = true,
                InstanceId = instance.Id,
                UtcNow = DateTimeOffset.UtcNow,
                AdminEnabled = true
            }));

        public Task<InstanceCallOutcome<AdminLiveStatsSnapshot>> GetStatsAsync(
            AdminInstanceOptions instance,
            CancellationToken cancellationToken = default)
        {
            StatsCallCountById[instance.Id] = StatsCallCountById.GetValueOrDefault(instance.Id) + 1;
            if (FailStats.Contains(instance.Id))
                return Task.FromResult(Fail<AdminLiveStatsSnapshot>(instance.Id, "down"));

            if (!Stats.TryGetValue(instance.Id, out AdminLiveStatsSnapshot? snap))
                snap = new AdminLiveStatsSnapshot
                {
                    InstanceId = instance.Id,
                    CollectedAtUtc = DateTimeOffset.UtcNow,
                    Domains = [],
                    UnassignedEndpoints = [],
                    Endpoints = []
                };

            return Task.FromResult(Ok(instance.Id, snap));
        }

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
