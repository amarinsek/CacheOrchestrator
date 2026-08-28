using CacheOrchestrator.Configuration;

namespace CacheOrchestrator.AspNetCore.UnitTests.Configuration;

public class ClientCacheHeaderGeneratorTests
{
    private static DomainHttpCacheOptions Cfg(
        ClientCacheability cacheability = ClientCacheability.Public,
        int ttl = 3600,
        int ttlMin = 60,
        DateTimeOffset? schedule = null,
        string? version = null,
        bool mustRevalidateNear = false)
        => new()
        {
            CoreOptions = new DomainCacheOptions
            {
                Domain = "test",
                Version = version ?? "1",
                DataCacheEnabled = true,
                DataCacheTtl = TimeSpan.FromSeconds(60),
            },
            ClientCacheability = cacheability,
            ClientTtlSeconds = ttl,
            ClientTtlMinSeconds = ttlMin,
            ScheduledUpdateUtc = schedule,
            ClientMustRevalidateNearUpdate = mustRevalidateNear,
            OutputCacheEnabled = true,
            OutputTtl = TimeSpan.FromSeconds(60),
            CacheableStatusCodes = [200],
            OutputCacheNamespace = "t",
            EncodingNormalizationList = null
        };

    // =========================
    // NoStore
    // =========================

    [Fact]
    public void NoStore_IgnoresSchedule_AndReturnsNoStore()
    {
        var schedule = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset now = schedule.AddDays(-10);
        ClientCacheHeaderGenerator.Result result = ClientCacheHeaderGenerator.Build(
            Cfg(ClientCacheability.NoStore, schedule: schedule), now);

        result.Header.Should().Be("no-store");
        result.MaxAgeSeconds.Should().Be(0);
        result.Phase.Should().Be(ClientCacheSchedulePhase.NotApplicable);
    }

    // =========================
    // No schedule
    // =========================

    [Fact]
    public void NoSchedule_UsesMaxTtl()
    {
        var now = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);
        ClientCacheHeaderGenerator.Result result = ClientCacheHeaderGenerator.Build(
            Cfg(ttl: 120, ttlMin: 30, schedule: null), now);

        result.Header.Should().Be("public, max-age=120");
        result.MaxAgeSeconds.Should().Be(120);
        result.Phase.Should().Be(ClientCacheSchedulePhase.NotApplicable);
    }

    [Fact]
    public void Private_NoSchedule_UsesPrivateDirective()
    {
        var now = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);
        ClientCacheHeaderGenerator.Result result = ClientCacheHeaderGenerator.Build(
            Cfg(ClientCacheability.Private, ttl: 90, ttlMin: 10), now);

        result.Header.Should().Be("private, max-age=90");
    }

    [Fact]
    public void ZeroTtl_EmitsMaxAgeZero_AndDisablesSchedule()
    {
        var schedule = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);

        ClientCacheHeaderGenerator.Result result = ClientCacheHeaderGenerator.Build(
            Cfg(ttl: 0, ttlMin: 60, schedule: schedule),
            schedule.AddHours(-1));

        result.Header.Should().Be("public, max-age=0");
        result.MaxAgeSeconds.Should().Be(0);
        result.Phase.Should().Be(ClientCacheSchedulePhase.NotApplicable);
    }

    // =========================
    // Calm (far from schedule)
    // =========================

    [Fact]
    public void FarFromSchedule_UsesMaxTtl_PhaseCalm()
    {
        var schedule = new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset now = schedule.AddSeconds(-10_000); // >> 3600
        ClientCacheHeaderGenerator.Result result = ClientCacheHeaderGenerator.Build(
            Cfg(ttl: 3600, ttlMin: 60, schedule: schedule), now);

        result.MaxAgeSeconds.Should().Be(3600);
        result.Phase.Should().Be(ClientCacheSchedulePhase.Calm);
        result.Header.Should().Be("public, max-age=3600");
    }

    // =========================
    // Approaching (ramp)
    // =========================

    [Fact]
    public void AtStartOfRampWindow_StillNearMax()
    {
        // secondsToSchedule == max ? edge of calm/ramp; implementation treats >= max as Calm
        var schedule = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset now = schedule.AddSeconds(-3600);
        ClientCacheHeaderGenerator.Result result = ClientCacheHeaderGenerator.Build(
            Cfg(ttl: 3600, ttlMin: 60, schedule: schedule), now);

        result.Phase.Should().Be(ClientCacheSchedulePhase.Calm);
        result.MaxAgeSeconds.Should().Be(3600);
    }

    [Fact]
    public void MidRamp_MaxAgeBetweenMinAndMax_PhaseApproaching()
    {
        var schedule = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        // halfway in time between min and max window: T = (60+3600)/2 = 1830
        DateTimeOffset now = schedule.AddSeconds(-1830);
        ClientCacheHeaderGenerator.Result result = ClientCacheHeaderGenerator.Build(
            Cfg(ttl: 3600, ttlMin: 60, schedule: schedule), now);

        result.Phase.Should().Be(ClientCacheSchedulePhase.Approaching);
        result.MaxAgeSeconds.Should().BeInRange(60, 3600);
        // linear: t=1830 ? roughly mid
        result.MaxAgeSeconds.Should().BeCloseTo(1830, 5);
    }

    [Fact]
    public void NearSchedule_MaxAgeAtMin_PhaseApproaching()
    {
        var schedule = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset now = schedule.AddSeconds(-60); // T == min
        ClientCacheHeaderGenerator.Result result = ClientCacheHeaderGenerator.Build(
            Cfg(ttl: 3600, ttlMin: 60, schedule: schedule), now);

        result.Phase.Should().Be(ClientCacheSchedulePhase.Approaching);
        result.MaxAgeSeconds.Should().Be(60);
    }

    [Fact]
    public void InsideLastMinuteBeforeSchedule_StaysAtMinFloor()
    {
        var schedule = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset now = schedule.AddSeconds(-1);
        ClientCacheHeaderGenerator.Result result = ClientCacheHeaderGenerator.Build(
            Cfg(ttl: 3600, ttlMin: 60, schedule: schedule), now);

        result.Phase.Should().Be(ClientCacheSchedulePhase.Approaching);
        result.MaxAgeSeconds.Should().Be(60);
    }

    [Fact]
    public void CacheabilityOverride_Private_WinsOverPublicConfig()
    {
        var now = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);
        ClientCacheHeaderGenerator.Result result = ClientCacheHeaderGenerator.Build(
            Cfg(ClientCacheability.Public, ttl: 90),
            now,
            ClientCacheability.Private);

        result.Header.Should().Be("private, max-age=90");
    }

    [Fact]
    public void MinGreaterThanMax_IsClampedToMax()
    {
        var schedule = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset now = schedule.AddSeconds(-30);
        ClientCacheHeaderGenerator.Result result = ClientCacheHeaderGenerator.Build(
            Cfg(ttl: 60, ttlMin: 3600, schedule: schedule), now);

        result.MaxAgeSeconds.Should().Be(60);
        result.Phase.Should().Be(ClientCacheSchedulePhase.Approaching);
    }

    [Fact]
    public void NearFloor_WithMustRevalidate_AppendsMustRevalidate()
    {
        var schedule = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset now = schedule.AddSeconds(-60);
        ClientCacheHeaderGenerator.Result result = ClientCacheHeaderGenerator.Build(
            Cfg(ttl: 3600, ttlMin: 60, schedule: schedule, mustRevalidateNear: true), now);

        result.Header.Should().Be("public, max-age=60, must-revalidate");
        result.Phase.Should().Be(ClientCacheSchedulePhase.Approaching);
    }

    // =========================
    // Hold (schedule passed)
    // =========================

    [Fact]
    public void AfterSchedule_UsesMin_PhaseHold()
    {
        var schedule = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset now = schedule.AddMinutes(5);
        ClientCacheHeaderGenerator.Result result = ClientCacheHeaderGenerator.Build(
            Cfg(ttl: 3600, ttlMin: 90, schedule: schedule), now);

        result.MaxAgeSeconds.Should().Be(90);
        result.Phase.Should().Be(ClientCacheSchedulePhase.Hold);
        result.Header.Should().Be("public, max-age=90");
    }

    [Fact]
    public void ZeroMinimum_AfterSchedule_EmitsMaxAgeZero()
    {
        var schedule = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);

        ClientCacheHeaderGenerator.Result result = ClientCacheHeaderGenerator.Build(
            Cfg(ttl: 3600, ttlMin: 0, schedule: schedule),
            schedule);

        result.Header.Should().Be("public, max-age=0");
        result.Phase.Should().Be(ClientCacheSchedulePhase.Hold);
    }

    [Fact]
    public void AfterSchedule_WithMustRevalidate_AppendsDirective()
    {
        var schedule = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset now = schedule.AddHours(1);
        ClientCacheHeaderGenerator.Result result = ClientCacheHeaderGenerator.Build(
            Cfg(ttl: 3600, ttlMin: 60, schedule: schedule, mustRevalidateNear: true), now);

        result.Header.Should().Contain("must-revalidate");
        result.Phase.Should().Be(ClientCacheSchedulePhase.Hold);
    }

    // HoldAfterVersion feature removed (Version is now a string, no time-math possible)

    // =========================
    // Edge: min == max
    // =========================

    [Fact]
    public void MinEqualsMax_AlwaysThatValue()
    {
        var schedule = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset now = schedule.AddSeconds(-500);
        ClientCacheHeaderGenerator.Result result = ClientCacheHeaderGenerator.Build(
            Cfg(ttl: 300, ttlMin: 300, schedule: schedule), now);

        result.MaxAgeSeconds.Should().Be(300);
    }

    // =========================
    // Notification / phase semantics (for host alerts)
    // =========================

    [Theory]
    [InlineData(10_000, ClientCacheSchedulePhase.Calm)]       // far
    [InlineData(1_800, ClientCacheSchedulePhase.Approaching)] // in ramp (max=3600,min=60)
    [InlineData(60, ClientCacheSchedulePhase.Approaching)]
    [InlineData(-10, ClientCacheSchedulePhase.Hold)]       // past schedule
    public void Phase_MatchesSecondsBeforeSchedule(int secondsBeforeSchedule, ClientCacheSchedulePhase expected)
    {
        var schedule = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset now = schedule.AddSeconds(-secondsBeforeSchedule);
        ClientCacheHeaderGenerator.Result result = ClientCacheHeaderGenerator.Build(
            Cfg(ttl: 3600, ttlMin: 60, schedule: schedule), now);

        result.Phase.Should().Be(expected);
    }

    /// <summary>
    /// Ramp window start: warning/info threshold for hosts =
    /// now >= ScheduledUpdateUtc - ClientTtlSeconds  ? phase is Approaching or Hold.
    /// </summary>
    [Fact]
    public void EnteringRampWindow_IsDetectableViaPhase()
    {
        var schedule = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        int ttl = 3600;
        DateTimeOffset justInside = schedule.AddSeconds(-(ttl - 1));
        DateTimeOffset justOutside = schedule.AddSeconds(-(ttl + 1));

        ClientCacheHeaderGenerator.Result inside = ClientCacheHeaderGenerator.Build(Cfg(ttl: ttl, ttlMin: 60, schedule: schedule), justInside);
        ClientCacheHeaderGenerator.Result outside = ClientCacheHeaderGenerator.Build(Cfg(ttl: ttl, ttlMin: 60, schedule: schedule), justOutside);

        outside.Phase.Should().Be(ClientCacheSchedulePhase.Calm);
        inside.Phase.Should().Be(ClientCacheSchedulePhase.Approaching);
    }
}
