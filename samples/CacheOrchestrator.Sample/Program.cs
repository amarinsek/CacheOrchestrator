using CacheOrchestrator.Bus;
using CacheOrchestrator.DependencyInjection;
using CacheOrchestrator.Diagnostics;
using CacheOrchestrator.Invalidation;
using CacheOrchestrator.Redis;
using CacheOrchestrator.Sample.Endpoints;
using CacheOrchestrator.Sample.Services;
using Microsoft.Extensions.Primitives;
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

// Multi-instance labs share appsettings.json on a RW volume. When either node saves,
// the peer reloads config; drop domain caches so new TTLs apply without a manual purge.
RegisterDomainInvalidateOnConfigReload(app);

app.MapFallbackToFile("index.html");
app.Run();

static void RegisterDomainInvalidateOnConfigReload(WebApplication app)
{
    IConfiguration config = app.Configuration;
    ICacheOrchestratorInvalidator inv = app.Services.GetRequiredService<ICacheOrchestratorInvalidator>();
    ILogger logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("CacheOrchestrator.Sample");

    ChangeToken.OnChange(
        config.GetReloadToken,
        () =>
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    List<DemoEndpointConfig> entries =
                        config.GetSection("Demo:Endpoints").Get<List<DemoEndpointConfig>>() ?? [];
                    foreach (string domain in entries
                                 .Select(e => e.Domain)
                                 .Where(d => !string.IsNullOrWhiteSpace(d))
                                 .Distinct(StringComparer.OrdinalIgnoreCase))
                    {
                        await inv.InvalidateDomainAsync(domain).ConfigureAwait(false);
                    }

                    logger.LogInformation("Configuration reloaded; demo domains invalidated.");
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Configuration reload invalidation failed (sample best-effort).");
                }
            });
        });
}
