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
            value = measurement;
            foreach (var tag in tags)
            {
                if (tag.Key == "domain")
                    domain = tag.Value?.ToString();
                if (tag.Key == "result")
                    result = tag.Value?.ToString();
            }
        });

        listener.Start();

        CacheOrchestratorMetrics.RecordFusion("catalog", "hit", 12);

        value.Should().Be(1);
        domain.Should().Be("catalog");
        result.Should().Be("hit");
    }

    [Fact]
    public void RecordFusion_WithDuration_RecordsHistogram()
    {
        double? duration = null;
        string? domain = null;
        string? result = null;

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
            duration = measurement;
            foreach (var tag in tags)
            {
                if (tag.Key == "domain")
                    domain = tag.Value?.ToString();
                if (tag.Key == "result")
                    result = tag.Value?.ToString();
            }
        });

        listener.Start();

        CacheOrchestratorMetrics.RecordFusion("products", "miss", 45.5);

        duration.Should().Be(45.5);
        domain.Should().Be("products");
        result.Should().Be("miss");
    }

    [Fact]
    public void RecordFusion_Bypass_WithoutDuration_DoesNotRequireHistogram()
    {
        long? value = null;
        string? result = null;

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
            value = measurement;
            foreach (var tag in tags)
            {
                if (tag.Key == "result")
                    result = tag.Value?.ToString();
            }
        });

        listener.Start();

        CacheOrchestratorMetrics.RecordFusion("catalog", "bypass");

        value.Should().Be(1);
        result.Should().Be("bypass");
    }

    [Fact]
    public void RecordOutput_IncrementsCounter_WithDomainAndResult()
    {
        long? value = null;
        string? domain = null;
        string? result = null;

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
            value = measurement;
            foreach (var tag in tags)
            {
                if (tag.Key == "domain")
                    domain = tag.Value?.ToString();
                if (tag.Key == "result")
                    result = tag.Value?.ToString();
            }
        });

        listener.Start();

        CacheOrchestratorMetrics.RecordOutput("catalog", "hit");

        value.Should().Be(1);
        domain.Should().Be("catalog");
        result.Should().Be("hit");
    }

    [Fact]
    public void RecordInvalidate_IncrementsCounter_WithDomain()
    {
        long? value = null;
        string? domain = null;

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
            value = measurement;
            foreach (var tag in tags)
            {
                if (tag.Key == "domain")
                    domain = tag.Value?.ToString();
            }
        });

        listener.Start();

        CacheOrchestratorMetrics.RecordInvalidate("catalog");

        value.Should().Be(1);
        domain.Should().Be("catalog");
    }

    [Fact]
    public void RecordInvalidate_RefusesPathLikeDomain()
    {
        long? value = null;

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
            value = measurement;
        });

        listener.Start();

        // Legacy bug: entity scope was passed as domain label — must not emit a series.
        CacheOrchestratorMetrics.RecordInvalidate("product-crud/products/42");

        value.Should().BeNull();
    }

    [Fact]
    public void RecordClientSchedule_IncrementsCounter_WithDomainAndPhase()
    {
        long? value = null;
        string? domain = null;
        string? phase = null;

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
            value = measurement;
            foreach (var tag in tags)
            {
                if (tag.Key == "domain")
                    domain = tag.Value?.ToString();
                if (tag.Key == "phase")
                    phase = tag.Value?.ToString();
            }
        });

        listener.Start();

        CacheOrchestratorMetrics.RecordClientSchedule("catalog", "hold-after-version");

        value.Should().Be(1);
        domain.Should().Be("catalog");
        phase.Should().Be("hold-after-version");
    }
}