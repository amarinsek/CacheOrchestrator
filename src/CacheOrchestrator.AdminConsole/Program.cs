using System.Text.Json.Serialization;
using CacheOrchestrator.AdminConsole.Models;
using CacheOrchestrator.AdminConsole.Options;
using CacheOrchestrator.AdminConsole.Services;
using CacheOrchestrator.AdminConsole.Services.Hints;
using CacheOrchestrator.AdminConsole.Services.Metrics;
using Scalar.AspNetCore;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// SPA expects Healthy/Degraded/Down strings (not 0/1/2).
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.Services
    .AddOptions<AdminConsoleOptions>()
    .Bind(builder.Configuration.GetSection(AdminConsoleOptions.SectionName))
    .Validate(o => o.RequestTimeoutMs is > 0 and <= 120_000, "AdminConsole:RequestTimeoutMs must be 1–120000.")
    .Validate(o => o.Parallelism is > 0 and <= 64, "AdminConsole:Parallelism must be 1–64.")
    .Validate(o => o.DownReprobeSeconds is >= 5 and <= 300, "AdminConsole:DownReprobeSeconds must be 5–300.")
    .Validate(o => o.Metrics.TimeoutMs is > 0 and <= 120_000, "AdminConsole:Metrics:TimeoutMs must be 1–120000.")
    .ValidateOnStart();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<InstanceReachabilityCache>();
builder.Services.AddHttpClient(LocalAdminClient.HttpClientName)
    .ConfigureHttpClient((sp, client) =>
    {
        AdminConsoleOptions opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<AdminConsoleOptions>>().Value;
        client.Timeout = TimeSpan.FromMilliseconds(Math.Max(500, opts.RequestTimeoutMs));
    });

builder.Services.AddHttpClient(PrometheusMetricsQueryClient.HttpClientName)
    .ConfigureHttpClient((sp, client) =>
    {
        AdminConsoleOptions opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<AdminConsoleOptions>>().Value;
        MetricsStoreOptions metrics = opts.Metrics;
        client.Timeout = TimeSpan.FromMilliseconds(Math.Max(500, metrics.TimeoutMs));
        if (!string.IsNullOrWhiteSpace(metrics.BaseUrl)
            && Uri.TryCreate(metrics.BaseUrl.Trim().TrimEnd('/'), UriKind.Absolute, out Uri? baseUri))
        {
            client.BaseAddress = baseUri;
        }
    });

builder.Services.AddSingleton<ILocalAdminClient, LocalAdminClient>();
builder.Services.AddSingleton<IHintRuleDisableStore, HintRuleDisableStore>();
builder.Services.AddSingleton<HintRuleRegistry>();
builder.Services.AddSingleton<HintEngine>();
builder.Services.AddSingleton<AdminFanOutService>();
builder.Services.AddSingleton<IMetricsQueryClient, PrometheusMetricsQueryClient>();
builder.Services.AddSingleton<MetricsQueryService>();
builder.Services.AddSingleton<MetricsWindowStatsService>();
builder.Services.AddSingleton<LiveStatsService>();

// OpenAPI + Scalar UI for operator host (all environments — Admin Console is not public internet by default).
builder.Services.AddOpenApi();

WebApplication app = builder.Build();

app.MapOpenApi();
// Default UI: /scalar  (Scalar.AspNetCore 2.x). Keep /scalar/v1 as a redirect for old bookmarks.
app.MapScalarApiReference(options =>
{
    options.WithTitle("CacheOrchestrator Admin Console");
    options.WithDefaultHttpClient(ScalarTarget.CSharp, ScalarClient.HttpClient);
});
app.MapGet("/scalar/v1", () => Results.Redirect("/scalar"));
app.MapGet("/scalar/v1/{**rest}", (string? rest) =>
    Results.Redirect(string.IsNullOrEmpty(rest) ? "/scalar" : $"/scalar/{rest}"));

app.UseDefaultFiles();
app.UseStaticFiles();

RouteGroupBuilder api = app.MapGroup("/api").WithTags("Admin Console");

api.MapGet("/about", () =>
{
    System.Reflection.Assembly asm = typeof(Program).Assembly;
    string? informational = asm
        .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
        .OfType<System.Reflection.AssemblyInformationalVersionAttribute>()
        .FirstOrDefault()
        ?.InformationalVersion;
    string version = informational ?? asm.GetName().Version?.ToString() ?? "dev";
    // MinVer may append +commit; keep display short.
    int plus = version.IndexOf('+', StringComparison.Ordinal);
    if (plus > 0)
        version = version[..plus];
    return Results.Ok(new { version, product = "CacheOrchestrator Admin Console" });
});

api.MapGet("/overview", async (AdminFanOutService fanOut, CancellationToken cancellationToken) =>
{
    OverviewDto overview = await fanOut.GetOverviewAsync(cancellationToken).ConfigureAwait(false);
    return Results.Ok(overview);
});

api.MapGet("/instances", async (AdminFanOutService fanOut, CancellationToken cancellationToken) =>
{
    IReadOnlyList<InstanceStatusDto> list = await fanOut.GetInstancesAsync(cancellationToken).ConfigureAwait(false);
    return Results.Ok(list);
});

api.MapGet("/distribution", async (AdminFanOutService fanOut, CancellationToken cancellationToken) =>
{
    ClusterDistributionCapabilityDto capability =
        await fanOut.GetDistributionCapabilityAsync(cancellationToken).ConfigureAwait(false);
    return Results.Ok(capability);
});

// Obsolete empty shell — SPA traffic stats use GET /api/stats/window (Prometheus).
api.MapGet("/stats", async (
    string? scope,
    bool? groupByInstance,
    string? instances,
    AdminFanOutService fanOut,
    CancellationToken cancellationToken) =>
{
    try
    {
#pragma warning disable CS0618
        ClusterStatsDto stats = await fanOut
            .GetStatsAsync(scope, cancellationToken, groupByInstance ?? false, instances)
            .ConfigureAwait(false);
#pragma warning restore CS0618
        return Results.Ok(stats);
    }
    catch (KeyNotFoundException ex)
    {
        return Results.NotFound(new { error = ex.Message });
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

// Obsolete empty list — SPA uses /api/stats/window for endpoint traffic.
api.MapGet("/endpoints", async (
    string? sort,
    int? take,
    int? skip,
    string? search,
    string? domain,
    string? domains,
    string? instances,
    long? minRequests,
    bool? groupByInstance,
    AdminFanOutService fanOut,
    CancellationToken cancellationToken) =>
{
    try
    {
#pragma warning disable CS0618
        IReadOnlyList<CacheOrchestrator.Admin.AdminEndpointStatsDto> list =
            await fanOut
                .GetTopEndpointsAsync(
                    sort,
                    take ?? 50,
                    cancellationToken,
                    groupByInstance ?? false,
                    search,
                    domain,
                    domains,
                    instances,
                    minRequests ?? 0,
                    skip ?? 0)
                .ConfigureAwait(false);
#pragma warning restore CS0618
        return Results.Ok(list);
    }
    catch (KeyNotFoundException ex)
    {
        return Results.NotFound(new { error = ex.Message });
    }
});

api.MapGet("/domains", async (AdminFanOutService fanOut, CancellationToken cancellationToken) =>
{
    FanOutResultDto<IReadOnlyList<CacheOrchestrator.Admin.AdminDomainConfigDto>> result =
        await fanOut.GetDomainsAsync(cancellationToken).ConfigureAwait(false);
    return Results.Ok(result);
});

api.MapPost("/invalidate", async (
    AdminConsoleInvalidateRequest body,
    AdminFanOutService fanOut,
    CancellationToken cancellationToken) =>
{
    try
    {
        FanOutResultDto<object?> result = await fanOut.InvalidateAsync(body, cancellationToken).ConfigureAwait(false);
        return Results.Ok(result);
    }
    catch (KeyNotFoundException ex)
    {
        return Results.NotFound(new { error = ex.Message });
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

api.MapPost("/domains/{domain}/version", async (
    string domain,
    AdminConsoleVersionRequest body,
    AdminFanOutService fanOut,
    CancellationToken cancellationToken) =>
{
    try
    {
        FanOutResultDto<object?> result =
            await fanOut.SetVersionAsync(domain, body, cancellationToken).ConfigureAwait(false);
        return Results.Ok(result);
    }
    catch (KeyNotFoundException ex)
    {
        return Results.NotFound(new { error = ex.Message });
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

api.MapMethods("/domains/{domain}/ttl", ["PATCH"], async (
    string domain,
    AdminConsoleTtlPatchRequest body,
    AdminFanOutService fanOut,
    CancellationToken cancellationToken) =>
{
    try
    {
        FanOutResultDto<object?> result =
            await fanOut.PatchTtlAsync(domain, body, cancellationToken).ConfigureAwait(false);
        return Results.Ok(result);
    }
    catch (KeyNotFoundException ex)
    {
        return Results.NotFound(new { error = ex.Message });
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

api.MapGet("/metrics/status", async (MetricsQueryService metrics, CancellationToken cancellationToken) =>
{
    MetricsStatusDto status = await metrics.GetStatusAsync(probe: true, cancellationToken).ConfigureAwait(false);
    return Results.Ok(status);
});

api.MapGet("/metrics/catalog", (MetricsQueryService metrics) => Results.Ok(metrics.GetCatalog()));

api.MapGet("/metrics/series", async (
    string? range,
    string? panels,
    string? domains,
    string? instances,
    string? routes,
    string? from,
    string? to,
    MetricsQueryService metrics,
    CancellationToken cancellationToken) =>
{
    try
    {
        MetricsSeriesResponseDto result = await metrics
            .GetSeriesAsync(range, panels, domains, instances, routes, from, to, cancellationToken)
            .ConfigureAwait(false);
        return Results.Ok(result);
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

api.MapGet("/metrics/summary", async (
    string? range,
    string? from,
    string? to,
    MetricsQueryService metrics,
    CancellationToken cancellationToken) =>
{
    MetricsSummaryDto summary = await metrics
        .GetSummaryAsync(range, from, to, cancellationToken)
        .ConfigureAwait(false);
    return Results.Ok(summary);
});

/// <summary>
/// Domain/endpoint stats for the selected Metrics time window (Prometheus), not process totals.
/// </summary>
api.MapGet("/stats/window", async (
    string? range,
    string? from,
    string? to,
    string? domains,
    MetricsWindowStatsService windowStats,
    CancellationToken cancellationToken) =>
{
    WindowStatsDto result = await windowStats
        .GetAsync(range, from, to, domains, cancellationToken)
        .ConfigureAwait(false);
    return Results.Ok(result);
});

/// <summary>
/// Live operational snapshot (fixed 1m Prometheus rates + instance health).
/// Independent of the global Range picker.
/// </summary>
api.MapGet("/live", async (LiveStatsService live, CancellationToken cancellationToken) =>
{
    LiveSnapshotDto snapshot = await live.GetAsync(cancellationToken).ConfigureAwait(false);
    return Results.Ok(snapshot);
});

api.MapGet("/hints/rules", (HintEngine engine, HintRuleRegistry registry) =>
{
    return Results.Ok(new
    {
        load = registry.GetLoadStatus(),
        rules = engine.GetCatalog(),
        knownPaths = CacheOrchestrator.AdminConsole.Services.Hints.Declarative.HintPathCatalog.All
            .OrderBy(p => p)
            .ToArray()
    });
});

api.MapPost("/hints/reload", (HintRuleRegistry registry) =>
{
    HintRuleLoadStatus status = registry.Reload();
    return Results.Ok(status);
});

api.MapPut("/hints/rules/{code}/enabled", async (
    string code,
    HintRuleEnableRequest body,
    IHintRuleDisableStore disable,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(code))
        return Results.BadRequest(new { error = "code is required." });
    await disable.SetEnabledAsync(code, body.Enabled, cancellationToken).ConfigureAwait(false);
    return Results.Ok(new { code, enabled = body.Enabled });
});

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "CacheOrchestrator.AdminConsole" }));

app.Run();

/// <summary>Exposes the entry assembly for WebApplicationFactory tests.</summary>
public partial class Program;
