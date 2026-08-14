namespace CacheOrchestrator.Admin.App.Services.Hints.Declarative;

/// <summary>Allowlisted paths for declarative rules (compiler checks these).</summary>
public static class HintPathCatalog
{
    private static readonly HashSet<string> Paths = new(StringComparer.OrdinalIgnoreCase)
    {
        "domain.requests",
        "domain.invalidations",
        "domain.invalidationShare",
        "domain.version",
        "domain.versionIsRuntimeOverride",
        "domain.schedulePhase",
        "domain.hasSchedule",
        "domain.factoryFailureRate",
        "domain.oc.hitShare",
        "domain.oc.hits",
        "domain.oc.misses",
        "domain.fc.hitShare",
        "domain.fc.originShare",
        "domain.fc.staleShare",
        "domain.fc.stale",
        "domain.fc.hits",
        "domain.fc.misses",
        "domain.fc.factoryRuns",
        "domain.fc.factoryFailures",
        "domain.fc.factoryFailureRate",
        "domain.instanceSpread.ocHitShare.stdev",
        "domain.instanceSpread.ocHitShare.sampleCount",
        "domain.instanceSpread.originShare.stdev",
        "domain.instanceSpread.originShare.sampleCount",
        "endpoint.route",
        "endpoint.requests",
        "endpoint.configuredDomain",
        "endpoint.factoryFailureRate",
        "endpoint.oc.hitShare",
        "endpoint.fc.hitShare",
        "endpoint.fc.originShare",
        "endpoint.fc.staleShare",
        "endpoint.fc.stale",
        "endpoint.fc.factoryRuns",
        "endpoint.fc.factoryFailures",
        "endpoint.fc.factoryFailureRate",
        "endpoint.instanceSpread.originShare.stdev",
        "endpoint.instanceSpread.originShare.sampleCount",
        "config.outputCacheTtlSeconds",
        "config.fusionCacheSoftTtlSeconds",
        "config.fusionCacheHardTtlSeconds",
        "config.fusionCacheFailSafeSeconds",
        "config.clientTtlSeconds",
        "config.clientTtlMinSeconds",
        "config.schedulePhase",
        "config.scheduledUpdateUtc",
        "config.versionIsRuntimeOverride",
        "config.hasSchedule",
        "config.holdAgeHours",
        "config.clientTtlOverOutputRatio",
        "config.clientTtlCannotRamp",
        "config.fusionHardLtSoft",
        "config.fusionCacheInstanceName",
    };

    public static bool IsKnown(string path) => Paths.Contains(path.Trim());

    public static IReadOnlyCollection<string> All => Paths;
}
