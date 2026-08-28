using System.Globalization;

namespace CacheOrchestrator.Configuration;

/// <summary>Builds Client Cache-Control headers from ASP.NET Core domain policy.</summary>
public static class ClientCacheHeaderGenerator
{
    /// <summary>Generated Client Cache header and schedule state.</summary>
    public readonly record struct Result(
        string Header,
        int MaxAgeSeconds,
        ClientCacheSchedulePhase Phase);

    /// <summary>Builds Cache-Control for the supplied policy and UTC time.</summary>
    public static Result Build(
        DomainHttpCacheOptions config,
        DateTimeOffset now,
        ClientCacheability? cacheabilityOverride = null)
    {
        ArgumentNullException.ThrowIfNull(config);

        ClientCacheability cacheability = cacheabilityOverride ?? config.ClientCacheability;
        if (cacheability == ClientCacheability.NoStore)
            return new Result("no-store", 0, ClientCacheSchedulePhase.NotApplicable);

        int max = Math.Max(0, config.ClientTtlSeconds);
        if (max == 0)
            return Finish(cacheability, 0, ClientCacheSchedulePhase.NotApplicable, mustRevalidate: false);

        int min = Math.Clamp(config.ClientTtlMinSeconds, 0, max);
        bool mustRevalidateNear = config.ClientMustRevalidateNearUpdate;

        if (config.ScheduledUpdateUtc is null)
            return Finish(cacheability, max, ClientCacheSchedulePhase.NotApplicable, mustRevalidate: false);

        DateTimeOffset schedule = config.ScheduledUpdateUtc.Value;
        if (now >= schedule)
            return Finish(cacheability, min, ClientCacheSchedulePhase.Hold, mustRevalidateNear);

        double secondsToSchedule = (schedule - now).TotalSeconds;
        if (secondsToSchedule >= max)
            return Finish(cacheability, max, ClientCacheSchedulePhase.Calm, mustRevalidate: false);

        int maxAge;
        if (max == min)
        {
            maxAge = min;
        }
        else
        {
            double time = Math.Clamp(secondsToSchedule, min, max);
            maxAge = (int)Math.Round(min + ((max - min) * (time - min) / (max - min)));
            maxAge = Math.Clamp(maxAge, min, max);
        }

        return Finish(
            cacheability,
            maxAge,
            ClientCacheSchedulePhase.Approaching,
            mustRevalidateNear && maxAge <= min);
    }

    private static Result Finish(
        ClientCacheability cacheability,
        int maxAge,
        ClientCacheSchedulePhase phase,
        bool mustRevalidate)
    {
        string directive = cacheability == ClientCacheability.Private ? "private" : "public";
        string maxAgeText = maxAge.ToString(CultureInfo.InvariantCulture);
        string header = mustRevalidate
            ? $"{directive}, max-age={maxAgeText}, must-revalidate"
            : $"{directive}, max-age={maxAgeText}";
        return new Result(header, maxAge, phase);
    }
}
