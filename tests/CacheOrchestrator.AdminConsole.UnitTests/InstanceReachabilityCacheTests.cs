using CacheOrchestrator.AdminConsole.Models;
using CacheOrchestrator.AdminConsole.Options;
using CacheOrchestrator.AdminConsole.Services;
using Microsoft.Extensions.Options;

namespace CacheOrchestrator.AdminConsole.UnitTests;

public class InstanceReachabilityCacheTests
{
    [Fact]
    public void ShouldSkipUnreachable_False_UntilFailureRecorded()
    {
        InstanceReachabilityCache sut = CreateSut(out _);
        sut.ShouldSkipUnreachable("a").Should().BeFalse();
    }

    [Fact]
    public void RecordFailure_SkipsUntilReprobeElapsed()
    {
        InstanceReachabilityCache sut = CreateSut(out TestMutableTimeProvider time, downReprobeSeconds: 15);

        sut.RecordFailure("a", "timeout", latencyMs: 3000);
        sut.ShouldSkipUnreachable("a").Should().BeTrue();
        CachedInstanceHealth? cached = sut.TryGetSkippedDown("a");
        cached.Should().NotBeNull();
        cached.Status.Should().Be(InstanceHealthStatus.Down);
        cached.Error.Should().Be("timeout");

        time.Advance(TimeSpan.FromSeconds(14));
        sut.ShouldSkipUnreachable("a").Should().BeTrue();

        time.Advance(TimeSpan.FromSeconds(2));
        sut.ShouldSkipUnreachable("a").Should().BeFalse();
        sut.TryGetSkippedDown("a").Should().BeNull();
    }

    [Fact]
    public void RecordSuccess_ClearsDownSkip()
    {
        InstanceReachabilityCache sut = CreateSut(out _);
        sut.RecordFailure("a", "down");
        sut.ShouldSkipUnreachable("a").Should().BeTrue();

        sut.RecordSuccess("a", reportedInstanceId: "reported-a", latencyMs: 12);
        sut.ShouldSkipUnreachable("a").Should().BeFalse();
    }

    [Fact]
    public void RecordHealth_Degraded_DoesNotSkip()
    {
        InstanceReachabilityCache sut = CreateSut(out _);
        sut.RecordHealth("a", InstanceHealthStatus.Degraded, error: "partial", latencyMs: 5, reportedInstanceId: "a");
        sut.ShouldSkipUnreachable("a").Should().BeFalse();
    }

    [Fact]
    public void DownReprobeSeconds_BelowFive_ClampsToFive()
    {
        InstanceReachabilityCache sut = CreateSut(out TestMutableTimeProvider time, downReprobeSeconds: 1);
        sut.DownReprobeInterval.Should().Be(TimeSpan.FromSeconds(5));

        sut.RecordFailure("a", "down");
        time.Advance(TimeSpan.FromSeconds(4));
        sut.ShouldSkipUnreachable("a").Should().BeTrue();
        time.Advance(TimeSpan.FromSeconds(2));
        sut.ShouldSkipUnreachable("a").Should().BeFalse();
    }

    private static InstanceReachabilityCache CreateSut(
        out TestMutableTimeProvider time,
        int downReprobeSeconds = 15)
    {
        time = new TestMutableTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        AdminConsoleOptions opts = new() { DownReprobeSeconds = downReprobeSeconds };
        return new InstanceReachabilityCache(Microsoft.Extensions.Options.Options.Create(opts), time);
    }
}
