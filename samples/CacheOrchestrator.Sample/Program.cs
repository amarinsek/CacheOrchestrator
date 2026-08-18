using CacheOrchestrator.Bus;
using CacheOrchestrator.DependencyInjection;
using CacheOrchestrator.Diagnostics;
using CacheOrchestrator.Redis;
using CacheOrchestrator.Sample.Endpoints;
using CacheOrchestrator.Sample.Services;
using OpenTelemetry.Metrics;

var builder = WebApplication.CreateBuilder(args);

// Peers in multi-instance labs share appsettings.json; poll so B reloads when A saves.
builder.Services.AddHostedService<AppSettingsPeerReloadService>();

// InMemory always; Redis + HTTP cluster bus when enabled in configuration (see labs/compose).
builder.Services.AddCacheOrchestrator(builder.Configuration, o =>
{
    o.AddRedisBackend();
    o.AddHttpClusterBus();
});

// Prometheus scrape endpoint for Admin Console App Metrics (compose labs or host scrape).
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics =>
    {
        metrics.AddMeter(CacheOrchestratorMetrics.MeterName);
        metrics.AddPrometheusExporter();
    });

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseCacheOrchestrator();

app.MapCacheOrchestratorAdmin(); // no-op unless Cache:Admin:Enabled
app.MapCacheOrchestratorHttpBus(); // no-op when Cache:Cluster:Bus:Enabled is false
// Base Output Cache policy would otherwise cache /metrics; Prometheus needs live samples.
app.MapPrometheusScrapingEndpoint() // GET /metrics
    .WithMetadata(new Microsoft.AspNetCore.OutputCaching.OutputCacheAttribute { NoStore = true });

app.MapDemoDataEndpoints(builder.Configuration);
app.MapDemoStudioEndpoints();

// Config reload only (no purge). Invalidation is a separate user action.
// DomainCacheOptionsProvider already refreshes options on IOptions change.

app.MapFallbackToFile("index.html");
app.Run();
