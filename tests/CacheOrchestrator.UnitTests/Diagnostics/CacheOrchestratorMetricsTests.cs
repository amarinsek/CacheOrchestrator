using CacheOrchestrator.Configuration;
using CacheOrchestrator.Diagnostics;
using System.Diagnostics.Metrics;

namespace CacheOrchestrator.UnitTests.Diagnostics;

public class CacheOrchestratorMetricsTests
{
    [Fact]
    public void RecordFusion_IncrementsCounter_WithDomainAndResult()
    {
        long? value = null;
        string? domain = null;
        string? result = null;
        const string expectedDomain = "ut-metrics-fc-hit";

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == CacheOrchestratorMetrics.MeterName &&
                instrument.Name == "cache_orchestrator.fc.requests")
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };

        listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
        {
            if (ReadTag(tags, "domain") != expectedDomain)
                return;
            value = measurement;
            domain = expectedDomain;
            result = ReadTag(tags, "result");
        });

        listener.Start();

        CacheOrchestratorMetrics.RecordFusion(expectedDomain, "hit", 12);

        value.Should().Be(1);
        domain.Should().Be(expectedDomain);
        result.Should().Be("hit");
    }

    [Fact]
    public void RecordFusion_WithDuration_RecordsHistogram()
    {
        double? duration = null;
        string? domain = null;
        string? result = null;
        const string expectedDomain = "ut-metrics-fc-duration";

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == CacheOrchestratorMetrics.MeterName &&
                instrument.Name == "cache_orchestrator.fc.duration")
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };

        listener.SetMeasurementEventCallback<double>((instrument, measurement, tags, state) =>
        {
            if (ReadTag(tags, "domain") != expectedDomain)
                return;
            duration = measurement;
            domain = expectedDomain;
            result = ReadTag(tags, "result");
        });

        listener.Start();

        CacheOrchestratorMetrics.RecordFusion(expectedDomain, "miss", 45.5);

        duration.Should().Be(45.5);
        domain.Should().Be(expectedDomain);
        result.Should().Be("miss");
    }

    [Fact]
    public void RecordFusion_Bypass_WithoutDuration_DoesNotRequireHistogram()
    {
        long? value = null;
        string? result = null;
        const string expectedDomain = "ut-metrics-fc-bypass";

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == CacheOrchestratorMetrics.MeterName &&
                instrument.Name == "cache_orchestrator.fc.requests")
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };

        listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
        {
            if (ReadTag(tags, "domain") != expectedDomain)
                return;
            value = measurement;
            result = ReadTag(tags, "result");
        });

        listener.Start();

        CacheOrchestratorMetrics.RecordFusion(expectedDomain, "bypass");

        value.Should().Be(1);
        result.Should().Be("bypass");
    }

    [Fact]
    public void RecordOutput_IncrementsCounter_WithDomainAndResult()
    {
        long? value = null;
        string? domain = null;
        string? result = null;
        const string expectedDomain = "ut-metrics-oc-hit";

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == CacheOrchestratorMetrics.MeterName &&
                instrument.Name == "cache_orchestrator.oc.requests")
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };

        listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
        {
            if (ReadTag(tags, "domain") != expectedDomain)
                return;
            value = measurement;
            domain = expectedDomain;
            result = ReadTag(tags, "result");
        });

        listener.Start();

        CacheOrchestratorMetrics.RecordOutput(expectedDomain, "hit");

        value.Should().Be(1);
        domain.Should().Be(expectedDomain);
        result.Should().Be("hit");
    }

    [Fact]
    public void RecordInvalidate_IncrementsCounter_WithDomain()
    {
        long? value = null;
        string? domain = null;
        const string expectedDomain = "ut-metrics-invalidate";

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == CacheOrchestratorMetrics.MeterName &&
                instrument.Name == "cache_orchestrator.invalidate")
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };

        listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
        {
            if (ReadTag(tags, "domain") != expectedDomain)
                return;
            value = measurement;
            domain = expectedDomain;
        });

        listener.Start();

        CacheOrchestratorMetrics.RecordInvalidate(expectedDomain);

        value.Should().Be(1);
        domain.Should().Be(expectedDomain);
    }

    [Fact]
    public void RecordInvalidate_RefusesPathLikeDomain()
    {
        long? value = null;
        const string pathLike = "product-crud/products/42";

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == CacheOrchestratorMetrics.MeterName &&
                instrument.Name == "cache_orchestrator.invalidate")
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };

        listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
        {
            if (ReadTag(tags, "domain") != pathLike)
                return;
            value = measurement;
        });

        listener.Start();

        // Legacy bug: entity scope was passed as domain label — must not emit a series.
        CacheOrchestratorMetrics.RecordInvalidate(pathLike);

        value.Should().BeNull();
    }

    [Fact]
    public void RecordClientSchedule_IncrementsCounter_WithDomainAndPhase()
    {
        long? value = null;
        string? domain = null;
        string? phase = null;
        const string expectedDomain = "ut-metrics-schedule-hold";

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == CacheOrchestratorMetrics.MeterName &&
                instrument.Name == "cache_orchestrator.client.schedule")
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };

        listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
        {
            if (ReadTag(tags, "domain") != expectedDomain)
                return;
            value = measurement;
            domain = expectedDomain;
            phase = ReadTag(tags, "phase");
        });

        listener.Start();

        CacheOrchestratorMetrics.RecordClientSchedule(
            expectedDomain,
            XCacheHeaderFormatter.PhaseToString(ClientCacheSchedulePhase.Hold));

        value.Should().Be(1);
        domain.Should().Be(expectedDomain);
        phase.Should().Be("hold");
    }

    private static string? ReadTag(ReadOnlySpan<KeyValuePair<string, object?>> tags, string key)
    {
        foreach (var tag in tags)
        {
            if (tag.Key == key)
                return tag.Value?.ToString();
        }

        return null;
    }
}
