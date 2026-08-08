using System.Globalization;

namespace CacheOrchestrator.Configuration;

/// <summary>
/// Builds client <c>Cache-Control</c> headers from domain options and the current time.
/// </summary>
public static class ClientCacheHeaderGenerator
{
    /// <summary>
    /// Result of building a client cache header.
    /// </summary>
    /// <param name="Header">Full Cache-Control header value.</param>
    /// <param name="MaxAgeSeconds">Computed max-age in seconds.</param>
    /// <param name="Phase">Schedule phase used for the computation.</param>
    public readonly record struct Result(
        string Header,
        int MaxAgeSeconds,
        ClientCacheSchedulePhase Phase);

    /// <summary>
    /// Builds a Cache-Control header for the given domain options at <paramref name="now"/>.
    /// </summary>
    /// <param name="config">Resolved domain options.</param>
    /// <param name="now">Current UTC time.</param>
    /// <param name="cacheabilityOverride">
    /// Optional override for <see cref="DomainCacheOptions.ClientCacheability"/>
    /// (e.g. force Private when the user is authenticated).
    /// </param>
    /// <returns>Header value, max-age, and schedule phase.</returns>
    public static Result Build(
        DomainCacheOptions config,
        DateTimeOffset now,
        ClientCacheability? cacheabilityOverride = null)
    {
        ArgumentNullException.ThrowIfNull(config);

        ClientCacheability cacheability = cacheabilityOverride ?? config.ClientCacheability;

        if (cacheability == ClientCacheability.NoStore)
        {
            return new Result("no-store", 0, ClientCacheSchedulePhase.NotApplicable);
        }

        int max = Math.Max(1, config.ClientTtlSeconds);
        int min = Math.Clamp(config.ClientTtlMinSeconds, 1, max);
        bool mustRevalidateNear = config.ClientMustRevalidateNearUpdate;

        // No schedule → always max TTL
        if (config.ScheduledUpdateUtc is null)
        {
            return Finish(
                cacheability,
                max,
                ClientCacheSchedulePhase.NotApplicable,
                mustRevalidate: false);
        }

        DateTimeOffset schedule = config.ScheduledUpdateUtc.Value;

        // Past scheduled update → floor at min until schedule is removed or updated
        if (now >= schedule)
        {
            return Finish(
                cacheability,
                min,
                ClientCacheSchedulePhase.Hold,
                mustRevalidateNear);
        }

        double secondsToSchedule = (schedule - now).TotalSeconds;

        // Far from update → max TTL
        if (secondsToSchedule >= max)
        {
            return Finish(
                cacheability,
                max,
                ClientCacheSchedulePhase.Calm,
                mustRevalidate: false);
        }

        // Ramp window: linear from max → min as we approach the schedule
        int maxAge;
        if (max == min)
        {
            maxAge = min;
        }
        else
        {
            double t = Math.Clamp(secondsToSchedule, min, max);
            maxAge = (int)Math.Round(min + ((max - min) * (t - min) / (max - min)));
            maxAge = Math.Clamp(maxAge, min, max);
        }

        bool nearFloor = maxAge <= min;
        return Finish(
            cacheability,
            maxAge,
            ClientCacheSchedulePhase.Approaching,
            mustRevalidateNear && nearFloor);
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