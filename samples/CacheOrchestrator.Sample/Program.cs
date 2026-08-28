using CacheOrchestrator.DependencyInjection;
using CacheOrchestrator.Diagnostics;
using CacheOrchestrator.HttpBus;
using CacheOrchestrator.Identity;
using CacheOrchestrator.Redis;
using CacheOrchestrator.Sample.Endpoints;
using CacheOrchestrator.Sample.Identity;
using CacheOrchestrator.Sample.Services;
using OpenTelemetry.Metrics;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Peers in multi-instance labs share appsettings.json; poll so B reloads when A saves.
builder.Services.AddHostedService<AppSettingsPeerReloadService>();

// InMemory always; Redis + HTTP cluster bus when enabled in configuration (see labs/compose).
builder.Services.AddCacheOrchestratorAspNetCore(builder.Configuration, o =>
{
    o.AddRedisBackend();
    o.AddHttpClusterBus();
});
builder.Services.AddCacheOrchestratorFusionCache(builder.Configuration);
builder.Services.AddCacheIdentityContract<ProductSearchIdentityContract>();

// Prometheus scrape endpoint for Admin Console App Metrics (compose labs or host scrape).
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics =>
    {
        metrics.AddMeter(CacheOrchestratorMetrics.MeterName);
        metrics.AddPrometheusExporter();
    });

WebApplication app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

// The UI uses this echo to distinguish a real server request from a response
// replayed by the browser HTTP cache. Register outside Output Cache so every
// network request gets its own value, including Output Cache hits.
const string demoRequestIdHeader = "X-Demo-Request-Id";
app.Use(async (http, next) =>
{
    string requestId = http.Request.Headers[demoRequestIdHeader].ToString();
    if (!string.IsNullOrWhiteSpace(requestId))
    {
        http.Response.OnStarting(() =>
        {
            http.Response.Headers[demoRequestIdHeader] = requestId;
            return Task.CompletedTask;
        });
    }

    await next(http);
});

app.UseCacheOrchestrator();

app.MapCacheOrchestratorAdmin(); // no-op unless Cache:Admin:Enabled
app.MapCacheOrchestratorHttpBus(); // no-op when Cache:Cluster:Bus:Enabled is false
// Base Output Cache policy would otherwise cache /metrics; Prometheus needs live samples.
app.MapPrometheusScrapingEndpoint() // GET /metrics
    .WithMetadata(new Microsoft.AspNetCore.OutputCaching.OutputCacheAttribute { NoStore = true });

app.MapGettingStartedEndpoints();
app.MapVaryDemoEndpoint();
app.MapPostIdentityDemoEndpoints();
app.MapDemoStudioEndpoints();

// Config reload only (no purge). Invalidation is a separate user action.
// DomainCacheOptionsProvider already refreshes options on IOptions change.

app.MapFallbackToFile("index.html");
app.Run();
