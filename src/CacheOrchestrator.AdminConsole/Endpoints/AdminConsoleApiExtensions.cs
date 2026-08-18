using CacheOrchestrator.Admin;
using CacheOrchestrator.AdminConsole.Models;
using CacheOrchestrator.AdminConsole.Services;
using CacheOrchestrator.AdminConsole.Services.Hints;
using CacheOrchestrator.AdminConsole.Services.Hints.Declarative;
using CacheOrchestrator.AdminConsole.Services.Metrics;

namespace CacheOrchestrator.AdminConsole.Endpoints;

/// <summary>Minimal API routes for the Admin Console BFF (<c>/api/*</c>).</summary>
public static class AdminConsoleApiExtensions
{
    /// <summary>Maps Console JSON API under <c>/api</c> and process <c>/health</c>.</summary>
    public static WebApplication MapAdminConsoleApi(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

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
            IReadOnlyList<InstanceStatusDto> list =
                await fanOut.GetInstancesAsync(cancellationToken).ConfigureAwait(false);
            return Results.Ok(list);
        });

        api.MapGet("/distribution", async (AdminFanOutService fanOut, CancellationToken cancellationToken) =>
        {
            ClusterDistributionCapabilityDto capability =
                await fanOut.GetDistributionCapabilityAsync(cancellationToken).ConfigureAwait(false);
            return Results.Ok(capability);
        });

        api.MapGet("/domains", async (AdminFanOutService fanOut, CancellationToken cancellationToken) =>
        {
            FanOutResultDto<IReadOnlyList<AdminDomainConfigDto>> result =
                await fanOut.GetDomainsAsync(cancellationToken).ConfigureAwait(false);
            return Results.Ok(result);
        });

        api.MapPost("/invalidate", async (
            AdminConsoleInvalidateRequest body,
            AdminFanOutService fanOut,
            CancellationToken cancellationToken) =>
            await ExecuteWriteAsync(
                    () => fanOut.InvalidateAsync(body, cancellationToken))
                .ConfigureAwait(false));

        api.MapPost("/domains/{domain}/version", async (
            string domain,
            AdminConsoleVersionRequest body,
            AdminFanOutService fanOut,
            CancellationToken cancellationToken) =>
            await ExecuteWriteAsync(
                    () => fanOut.SetVersionAsync(domain, body, cancellationToken))
                .ConfigureAwait(false));

#pragma warning disable CS0618 // TTL route + DTO kept as compatible wrappers
        // Prefer PATCH /api/domains/{domain}/settings. This route remains for compatibility.
        api.MapMethods("/domains/{domain}/ttl", ["PATCH"], async (
            string domain,
            AdminConsoleTtlPatchRequest body,
            AdminFanOutService fanOut,
            CancellationToken cancellationToken) =>
            await ExecuteWriteAsync(
                    () => fanOut.PatchTtlAsync(domain, body, cancellationToken))
                .ConfigureAwait(false))
            .WithSummary("Patch domain TTL (obsolete — use /domains/{domain}/settings)")
            .WithDescription(
                "Obsolete. Prefer PATCH /api/domains/{domain}/settings with a sparse settings map. " +
                "This endpoint remains for compatibility and maps onto the same runtime overlay.");
#pragma warning restore CS0618

        api.MapGet("/domain-settings/catalog", async (
            AdminFanOutService fanOut,
            CancellationToken cancellationToken) =>
        {
            AdminDomainSettingsCatalogDto catalog =
                await fanOut.GetDomainSettingsCatalogAsync(cancellationToken).ConfigureAwait(false);
            return Results.Ok(catalog);
        });

        api.MapMethods("/domains/{domain}/settings", ["PATCH"], async (
            string domain,
            AdminConsoleSettingsPatchRequest body,
            AdminFanOutService fanOut,
            CancellationToken cancellationToken) =>
            await ExecuteWriteAsync(
                    () => fanOut.PatchSettingsAsync(domain, body, cancellationToken))
                .ConfigureAwait(false));

        api.MapGet("/metrics/status", async (MetricsQueryService metrics, CancellationToken cancellationToken) =>
        {
            MetricsStatusDto status =
                await metrics.GetStatusAsync(probe: true, cancellationToken).ConfigureAwait(false);
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

        api.MapGet("/live", async (LiveStatsService live, CancellationToken cancellationToken) =>
        {
            LiveSnapshotDto snapshot = await live.GetAsync(cancellationToken).ConfigureAwait(false);
            return Results.Ok(snapshot);
        });

        api.MapGet("/hints/rules", (HintEngine engine, HintRuleRegistry registry) =>
            Results.Ok(new HintRulesResponseDto
            {
                Load = registry.GetLoadStatus(),
                Rules = engine.GetCatalog(),
                KnownPaths = HintPathCatalog.All.OrderBy(p => p, StringComparer.Ordinal).ToArray(),
            }));

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
        return app;
    }

    private static async Task<IResult> ExecuteWriteAsync(Func<Task<FanOutResultDto<object?>>> action)
    {
        try
        {
            FanOutResultDto<object?> result = await action().ConfigureAwait(false);
            if (string.Equals(result.Outcome, WriteOutcomes.Success, StringComparison.Ordinal))
                return Results.Ok(result);

            return Results.Json(result, statusCode: StatusCodes.Status409Conflict);
        }
        catch (KeyNotFoundException ex)
        {
            return Results.NotFound(new { error = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }
}
