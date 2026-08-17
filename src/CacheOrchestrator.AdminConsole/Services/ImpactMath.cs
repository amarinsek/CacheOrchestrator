using CacheOrchestrator.Admin;

namespace CacheOrchestrator.AdminConsole.Services;

/// <summary>
/// Console-only impact KPIs from raw/lifetime counters (no OTel required).
/// Window is whatever counters represent (typically process lifetime until poll-delta/Prom).
/// </summary>
public static class ImpactMath
{
    /// <summary>Same threshold as <see cref="AdminStatsMath.LowSampleThreshold"/> for request shares.</summary>
    public const int LowRequestSampleThreshold = AdminStatsMath.LowSampleThreshold;

    /// <summary>Minimum factory duration samples before time-saved estimates are trusted.</summary>
    public const int LowDurationSampleThreshold = 5;

    /// <summary>Minimum result-size samples before payload offload is trusted.</summary>
    public const int LowSizeSampleThreshold = 5;

    /// <summary>Avg factory duration (ms) below this is "low" cost.</summary>
    public const double LowDurationMs = 5;

    /// <summary>Avg factory duration (ms) above this is "high" cost.</summary>
    public const double HighDurationMs = 50;

    /// <summary>Avg result size (bytes) below this is "low" cost.</summary>
    public const double LowSizeBytes = 1024;

    /// <summary>Avg result size (bytes) above this is "high" cost.</summary>
    public const double HighSizeBytes = 100 * 1024;

    /// <summary>Absolute request count treated as very low traffic (lifetime proxy without a time window).</summary>
    public const long VeryLowRequestCount = 20;

    /// <summary>Absolute request count treated as high traffic (lifetime proxy).</summary>
    public const long HighRequestCount = 1000;

    public static CacheImpactKpiDto Compute(
        long requests,
        long factoryRuns,
        double? factoryDurationSumMs,
        long factoryDurationCount,
        long? factoryResultSizeSumBytes = null,
        long factoryResultSizeCount = 0)
    {
        bool lowRequest = requests is > 0 and < LowRequestSampleThreshold || requests == 0;
        bool lowDuration = factoryDurationCount < LowDurationSampleThreshold;
        bool lowSize = factoryResultSizeCount < LowSizeSampleThreshold;

        double? factoryShare = requests > 0 ? (double)factoryRuns / requests : null;
        double? avoidance = requests > 0 ? 1.0 - (double)factoryRuns / requests : null;

        double? avgMs = factoryDurationCount > 0 && factoryDurationSumMs is double sum
            ? sum / factoryDurationCount
            : null;

        double? avgSize = factoryResultSizeCount > 0 && factoryResultSizeSumBytes is long sizeSum
            ? (double)sizeSum / factoryResultSizeCount
            : null;

        long avoided = Math.Max(0, requests - factoryRuns);
        double? timeSavedMs = avgMs is double a ? avoided * a : null;
        double? payloadOffload = avgSize is double s ? avoided * s : null;
        double? paidMs = factoryDurationSumMs;

        double? timeSavedRatio = null;
        if (timeSavedMs is double ts && paidMs is double paid)
        {
            double denom = ts + paid;
            if (denom > 0)
                timeSavedRatio = ts / denom;
        }
        else if (timeSavedMs is double tsOnly && tsOnly > 0 && factoryRuns == 0)
        {
            timeSavedRatio = 1.0;
        }

        string durationCost = CostLevelDuration(avgMs, lowDuration);
        string sizeCost = CostLevelSize(avgSize, lowSize);
        string cost = MaxCost(durationCost, sizeCost);
        string benefit = BenefitBand(avoidance, factoryShare, cost, lowRequest);
        string candidate = CandidateBand(requests, factoryShare, cost, lowRequest);

        return new CacheImpactKpiDto
        {
            FactoryAvoidance = avoidance,
            FactoryShare = factoryShare,
            AvgFactoryDurationMs = avgMs,
            EstFactoryTimeSavedMs = timeSavedMs,
            TimeSavedRatio = timeSavedRatio,
            FactoryDurationSumMs = factoryDurationSumMs,
            FactoryDurationCount = factoryDurationCount,
            AvgFactoryResultSizeBytes = avgSize,
            EstPayloadOffloadBytes = payloadOffload,
            FactoryResultSizeSumBytes = factoryResultSizeSumBytes,
            FactoryResultSizeCount = factoryResultSizeCount,
            Benefit = benefit,
            Candidate = candidate,
            LowRequestSample = lowRequest,
            LowDurationSample = lowDuration,
            LowSizeSample = lowSize
        };
    }

    /// <summary>Cost level from average factory duration: LOW / MEDIUM / HIGH / UNKNOWN.</summary>
    public static string CostLevelDuration(double? avgFactoryDurationMs, bool lowDurationSample)
    {
        if (lowDurationSample || avgFactoryDurationMs is not double ms)
            return "UNKNOWN";
        if (ms < LowDurationMs)
            return "LOW";
        if (ms > HighDurationMs)
            return "HIGH";
        return "MEDIUM";
    }

    /// <summary>Cost level from average result size: LOW / MEDIUM / HIGH / UNKNOWN.</summary>
    public static string CostLevelSize(double? avgBytes, bool lowSizeSample)
    {
        if (lowSizeSample || avgBytes is not double b)
            return "UNKNOWN";
        if (b < LowSizeBytes)
            return "LOW";
        if (b > HighSizeBytes)
            return "HIGH";
        return "MEDIUM";
    }

    /// <summary>Picks the higher of two cost levels (UNKNOWN is ignored unless both unknown).</summary>
    public static string MaxCost(string a, string b)
    {
        int Ra(string c) => c switch
        {
            "HIGH" => 3,
            "MEDIUM" => 2,
            "LOW" => 1,
            _ => 0
        };
        int ra = Ra(a);
        int rb = Ra(b);
        if (ra == 0 && rb == 0)
            return "UNKNOWN";
        return ra >= rb ? a : b;
    }

    public static string BenefitBand(
        double? factoryAvoidance,
        double? factoryShare,
        string costLevel,
        bool lowRequestSample)
    {
        if (lowRequestSample)
            return "UNKNOWN";

        double avoidance = factoryAvoidance ?? 0;
        double share = factoryShare ?? 0;
        bool highAvoid = avoidance >= 0.75;
        bool lowAvoid = share >= 0.25; // factory often

        return (highAvoid, lowAvoid, costLevel) switch
        {
            (true, _, "HIGH") => "HIGH",
            (true, _, "MEDIUM") => "MEDIUM",
            (true, _, "LOW") => "LOW_GAIN",
            (true, _, _) => "MEDIUM",
            (_, true, "HIGH") => "AT_RISK",
            (_, true, "MEDIUM") => "AT_RISK",
            (_, true, "LOW") => "LOW",
            _ => "LOW"
        };
    }

    public static string CandidateBand(
        long requests,
        double? factoryShare,
        string costLevel,
        bool lowRequestSample)
    {
        if (lowRequestSample || requests == 0)
            return "INSUFFICIENT_DATA";

        if (factoryShare is double fs && fs >= 0.25)
            return "NEEDS_TUNING";

        bool veryLowTraffic = requests < VeryLowRequestCount;
        bool highTraffic = requests >= HighRequestCount;
        bool lowCost = costLevel is "LOW" or "UNKNOWN";
        bool highCost = costLevel == "HIGH";

        if (veryLowTraffic && lowCost)
            return "POOR";
        if (veryLowTraffic && highCost)
            return "LIMITED";
        if (highTraffic && highCost)
            return "STRONG";
        if (highTraffic && lowCost)
            return "VOLUME";
        if (highCost)
            return "LIMITED";
        return "LIMITED";
    }
}
