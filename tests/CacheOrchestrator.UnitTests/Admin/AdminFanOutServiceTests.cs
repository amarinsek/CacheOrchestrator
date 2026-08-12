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
    public async Task InvalidateAsync_FansOutToResolvedTargets()
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

        result.Results.Should().ContainSingle(r => r.InstanceId == "a" && r.Succeeded);
        client.InvalidateCalls.Should().ContainSingle(c => c == "a");
    }

    private static AdminFanOutService CreateSut(params AdminInstanceOptions[] instances) =>
        CreateSut(new FakeLocalAdminClient(), instances);

    private static AdminFanOutService CreateSut(ILocalAdminClient client, params AdminInstanceOptions[] instances)
    {
        CacheAdminOptions opts = new()
        {
            Instances = instances.ToList(),
            Parallelism = 4,
            RequestTimeoutMs = 1000
        };
        return new AdminFanOutService(client, Options.Create(opts));
    }

    private sealed class FakeLocalAdminClient : ILocalAdminClient
    {
        public Dictionary<string, AdminLiveStatsSnapshot> Stats { get; } = new(StringComparer.Ordinal);
        public HashSet<string> FailStats { get; } = new(StringComparer.Ordinal);
        public List<string> InvalidateCalls { get; } = [];

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
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Ok(instance.Id, (IReadOnlyList<AdminDomainConfigDto>)[]));

        public Task<InstanceCallOutcome<CacheInvalidationResult>> InvalidateAsync(
            AdminInstanceOptions instance,
            AdminInvalidateRequest body,
            CancellationToken cancellationToken = default)
        {
            InvalidateCalls.Add(instance.Id);
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
