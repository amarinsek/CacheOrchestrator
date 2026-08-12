namespace CacheOrchestrator.Admin;

/// <summary>Zero-work collector used when Admin is disabled.</summary>
internal sealed class NoOpAdminStatsCollector : IAdminStatsCollector
{
    public static readonly NoOpAdminStatsCollector Instance = new();

    private NoOpAdminStatsCollector()
    {
    }

    public bool IsEnabled => false;

    public bool TrackEndpoints => false;

    public bool TrackLatency => false;

    public void RecordOutput(string? endpointKey, string? domain, string result)
    {
    }

    public void RecordFusion(string? endpointKey, string? domain, string result, long? elapsedTicks = null)
    {
    }

    public void RecordInvalidation(string domain)
    {
    }

    public AdminLiveStatsSnapshot GetSnapshot() =>
        new()
        {
            InstanceId = string.Empty,
            CollectedAtUtc = DateTimeOffset.UtcNow,
            Domains = [],
            UnassignedEndpoints = []
        };
}
