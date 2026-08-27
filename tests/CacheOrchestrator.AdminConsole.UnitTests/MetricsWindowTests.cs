using CacheOrchestrator.AdminConsole.Models;
using CacheOrchestrator.AdminConsole.Services.Metrics;

namespace CacheOrchestrator.AdminConsole.UnitTests;

public class MetricsWindowTests
{
    [Theory]
    [InlineData("15s", 15)]
    [InlineData("30s", 30)]
    [InlineData("1m", 60)]
    [InlineData("2m", 120)]
    [InlineData("15m", 900)]
    [InlineData("1h", 3600)]
    [InlineData("bad", 0)]
    [InlineData("", 0)]
    [InlineData(null, 0)]
    public void ParseStepSeconds(string? step, long expected) =>
        Assert.Equal(expected, MetricsRange.ParseStepSeconds(step));

    [Fact]
    public void Resolve_relative_snaps_end_to_step_and_keeps_duration()
    {
        // 1h → step 30s. now at :07 past a 30s boundary → end floors to :00.
        DateTimeOffset now = new(2026, 3, 18, 12, 00, 37, TimeSpan.Zero);
        var w = MetricsWindow.Resolve("1h", from: null, to: null, now);

        Assert.Equal("1h", w.RangeLabel);
        Assert.Equal("30s", w.Step);
        Assert.Equal(new DateTimeOffset(2026, 3, 18, 12, 00, 30, TimeSpan.Zero), w.End);
        Assert.Equal(new DateTimeOffset(2026, 3, 18, 11, 00, 30, TimeSpan.Zero), w.Start);
        Assert.Equal(TimeSpan.FromHours(1), w.Duration);
    }

    [Fact]
    public void Resolve_relative_stable_across_sub_step_clock_advance()
    {
        DateTimeOffset t0 = new(2026, 3, 18, 12, 00, 37, TimeSpan.Zero);
        var a = MetricsWindow.Resolve("6h", null, null, t0);
        var b = MetricsWindow.Resolve("6h", null, null, t0.AddSeconds(5));
        var c = MetricsWindow.Resolve("6h", null, null, t0.AddSeconds(20));

        Assert.Equal(a.Start, b.Start);
        Assert.Equal(a.End, b.End);
        Assert.Equal(a.Start, c.Start);
        Assert.Equal(a.End, c.End);

        // Crossing the 1m step boundary moves the snapped window once.
        var d = MetricsWindow.Resolve("6h", null, null, t0.AddSeconds(30));
        Assert.True(d.End > a.End);
        Assert.Equal(TimeSpan.FromMinutes(1), d.End - a.End);
        Assert.Equal(TimeSpan.FromHours(6), d.Duration);
    }

    [Fact]
    public void Resolve_absolute_floors_both_ends_to_step()
    {
        string from = "2026-01-01T00:00:07Z";
        string to = "2026-01-01T01:00:13Z";
        var w = MetricsWindow.Resolve(null, from, to, DateTimeOffset.UtcNow);

        Assert.Equal("custom", w.RangeLabel);
        Assert.True(w.IsAbsolute);
        Assert.Equal("30s", w.Step); // ~1h → 30s step
        Assert.Equal(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), w.Start);
        Assert.Equal(new DateTimeOffset(2026, 1, 1, 1, 0, 0, TimeSpan.Zero), w.End);
    }

    [Fact]
    public void Resolve_absolute_already_aligned_unchanged()
    {
        string from = "2026-01-01T00:00:00Z";
        string to = "2026-01-01T01:00:00Z";
        var w = MetricsWindow.Resolve(null, from, to, DateTimeOffset.UtcNow);

        Assert.Equal(DateTimeOffset.Parse(from), w.Start);
        Assert.Equal(DateTimeOffset.Parse(to), w.End);
    }

    [Fact]
    public void FloorUnixToStep_examples()
    {
        Assert.Equal(100, MetricsRange.FloorUnixToStep(100, 1));
        Assert.Equal(90, MetricsRange.FloorUnixToStep(97, 30));
        Assert.Equal(120, MetricsRange.FloorUnixToStep(120, 30));
    }
}
