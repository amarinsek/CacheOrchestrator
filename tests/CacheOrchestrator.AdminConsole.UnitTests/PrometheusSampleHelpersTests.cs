using CacheOrchestrator.AdminConsole.Services.Metrics;

namespace CacheOrchestrator.AdminConsole.UnitTests;

public class PrometheusSampleHelpersTests
{
    [Fact]
    public void Label_TrimsAndFallsBackToEmpty()
    {
        Dictionary<string, string> metric = new() { ["domain"] = "  catalog  ", ["empty"] = " " };
        PrometheusSampleHelpers.Label(metric, "domain").Should().Be("catalog");
        PrometheusSampleHelpers.Label(metric, "empty").Should().BeEmpty();
        PrometheusSampleHelpers.Label(metric, "missing").Should().BeEmpty();
    }

    [Theory]
    [InlineData(null, 0)]
    [InlineData(double.NaN, 0)]
    [InlineData(double.PositiveInfinity, 0)]
    [InlineData(-1.0, 0)]
    [InlineData(0.0, 0)]
    [InlineData(1.4, 1)]
    [InlineData(1.6, 2)]
    public void ToCount(double? value, long expected) =>
        PrometheusSampleHelpers.ToCount(value).Should().Be(expected);

    [Fact]
    public void FirstValue_SkipsNaNAndNegatives()
    {
        static PrometheusInstantSample Sample(double? v) =>
            new() { Metric = new Dictionary<string, string>(), Value = v };

        PrometheusSampleHelpers.FirstValue([]).Should().BeNull();
        PrometheusSampleHelpers.FirstValue([Sample(double.NaN)]).Should().BeNull();
        PrometheusSampleHelpers.FirstValue([Sample(-3)]).Should().Be(0);
        PrometheusSampleHelpers.FirstValue([Sample(1.25)]).Should().Be(1.25);
    }

    [Fact]
    public void ParseCsv_TrimsAndDropsEmpty()
    {
        PrometheusSampleHelpers.ParseCsv(null).Should().BeEmpty();
        PrometheusSampleHelpers.ParseCsv("  ").Should().BeEmpty();
        PrometheusSampleHelpers.ParseCsv("a, b,,c").Should().Equal("a", "b", "c");
    }
}
