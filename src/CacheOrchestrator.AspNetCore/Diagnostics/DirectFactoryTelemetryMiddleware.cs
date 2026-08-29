using CacheOrchestrator.Admin;
using CacheOrchestrator.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;

namespace CacheOrchestrator.Diagnostics;

/// <summary>
/// Attributes direct application work when Output Cache did not serve the response and no Data Cache operation ran.
/// </summary>
internal sealed class DirectFactoryTelemetryMiddleware
{
    private readonly RequestDelegate _next;

    public DirectFactoryTelemetryMiddleware(RequestDelegate next)
    {
        ArgumentNullException.ThrowIfNull(next);
        _next = next;
    }

    public async Task InvokeAsync(HttpContext http)
    {
        ICacheOrchestratorFeature? cacheFeature = http.Features.Get<ICacheOrchestratorFeature>();
        CacheFactoryExecutionFeature? execution = http.Features.Get<CacheFactoryExecutionFeature>();
        string? domain = cacheFeature?.DomainOptions?.Domain ?? execution?.DirectFactoryDomain;
        if (domain is null)
        {
            // Perf/allocation optimization: unrelated endpoints must not allocate request telemetry state.
            await _next(http).ConfigureAwait(false);
            return;
        }

        execution ??= CacheFactoryExecutionFeatureAccessor.GetOrCreate(http);
        execution.DirectFactoryDomain = domain;
        execution.DirectFactoryStartedTimestamp = Stopwatch.GetTimestamp();
        http.Response.OnStarting(RecordSuccessfulFactoryAsync, (http, execution));

        try
        {
            await _next(http).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            RecordDirectFactory(http, execution, failed: true);
            throw;
        }
    }

    private static Task RecordSuccessfulFactoryAsync(object state)
    {
        (HttpContext http, CacheFactoryExecutionFeature execution) =
            ((HttpContext, CacheFactoryExecutionFeature))state;
        RecordDirectFactory(http, execution, failed: false);
        return Task.CompletedTask;
    }

    private static void RecordDirectFactory(
        HttpContext http,
        CacheFactoryExecutionFeature execution,
        bool failed)
    {
        if (execution.DataCacheObserved || execution.DirectFactoryRecorded)
            return;

        execution.DirectFactoryRecorded = true;
        long elapsedTicks = Stopwatch.GetTimestamp() - execution.DirectFactoryStartedTimestamp;
        double elapsedMs = elapsedTicks * 1000d / Stopwatch.Frequency;

        ICacheOrchestratorFeature feature = CacheOrchestratorFeatureAccessor.GetOrCreate(http);
        string domain = feature.DomainOptions?.Domain ?? execution.DirectFactoryDomain ?? "_";

        if (!failed)
        {
            CacheDisposition disposition = feature.Disposition ??= new CacheDisposition();
            disposition.ElapsedMs = (long)elapsedMs;
        }

        IAdminStatsCollector? adminStats = http.RequestServices.GetService<IAdminStatsCollector>();
        bool adminOn = adminStats is { IsEnabled: true };
        CacheOrchestratorMetricsHttpExtensions.ResolveEndpointKeys(
            http,
            forAdminStats: adminOn,
            forMetrics: CacheOrchestratorMetrics.IsFactoryEnabled,
            out string? endpointKey,
            out string? metricsRoute);

        CacheOrchestratorMetrics.RecordFactory(
            domain,
            failed,
            elapsedMs,
            metricsRoute,
            http.Response.ContentLength);

        if (adminOn)
        {
            adminStats!.RecordFactory(
                endpointKey,
                domain,
                failed,
                adminStats.TrackLatency ? elapsedTicks : null,
                adminStats.TrackResultSize ? http.Response.ContentLength : null);
        }
    }
}
