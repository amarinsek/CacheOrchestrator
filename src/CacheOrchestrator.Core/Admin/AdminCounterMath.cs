namespace CacheOrchestrator.Admin;

internal static class AdminCounterMath
{
    public static long Requests(
        long outputCacheHits,
        long outputCacheMisses,
        long outputCacheBypass,
        long dataCacheHits,
        long dataCacheMisses,
        long dataCacheStale,
        long dataCacheBypass,
        long outputCacheOff,
        long factoryRuns) =>
        Math.Max(
            outputCacheHits + outputCacheMisses + outputCacheBypass + outputCacheOff,
            Math.Max(
                dataCacheHits + dataCacheMisses + dataCacheStale + dataCacheBypass,
                factoryRuns));
}
