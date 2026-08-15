using CacheOrchestrator.DependencyInjection;
using CacheOrchestrator.Diagnostics;
using CacheOrchestrator.Redis;
using CacheOrchestrator.Sample.Endpoints;
using OpenTelemetry.Metrics;

var builder = WebApplication.CreateBuilder(args);

// InMemory is always available; AddRedisBackend enables "Provider": "Redis" in appsettings.
builder.Services.AddCacheOrchestrator(builder.Configuration, o => o.AddRedisBackend());

// Prometheus scrape endpoint for Admin Console App Metrics (see deploy/prometheus under this sample).
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
// Base Output Cache policy would otherwise cache /metrics; Prometheus needs live samples.
app.MapPrometheusScrapingEndpoint() // GET /metrics
    .WithMetadata(new Microsoft.AspNetCore.OutputCaching.OutputCacheAttribute { NoStore = true });

app.MapDemoDataEndpoints(builder.Configuration);
app.MapDemoStudioEndpoints();

app.MapFallbackToFile("index.html");
app.Run();
