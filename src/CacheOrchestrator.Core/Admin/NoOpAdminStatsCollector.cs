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

    public bool TrackResultSize => false;

    public void RecordOutput(string? endpointKey, string? domain, string result)
    {
    }

    public void RecordDataCache(
        string? endpointKey,
        string? domain,
        string result,
        long? elapsedTicks = null,
        long? resultSizeBytes = null)
    {
    }

    public void RecordFactory(AdminFactoryRecord record)
    {
    }

    public void RecordInvalidation(string domain)
    {
    }

    public AdminLiveStatsRawSnapshot GetRawSnapshot() =>
        new()
        {
            InstanceId = string.Empty,
            CollectedAtUtc = DateTimeOffset.UtcNow,
            Domains = [],
            UnassignedEndpoints = [],
            Endpoints = []
        };

}
