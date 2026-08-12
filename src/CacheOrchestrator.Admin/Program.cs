using CacheOrchestrator.Admin.App.Models;
using CacheOrchestrator.Admin.App.Options;
using CacheOrchestrator.Admin.App.Services;
using Scalar.AspNetCore;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddOptions<CacheAdminOptions>()
    .Bind(builder.Configuration.GetSection(CacheAdminOptions.SectionName))
    .Validate(o => o.RequestTimeoutMs is > 0 and <= 120_000, "CacheAdmin:RequestTimeoutMs must be 1–120000.")
    .Validate(o => o.Parallelism is > 0 and <= 64, "CacheAdmin:Parallelism must be 1–64.")
    .ValidateOnStart();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHttpClient(LocalAdminClient.HttpClientName)
    .ConfigureHttpClient((sp, client) =>
    {
        CacheAdminOptions opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<CacheAdminOptions>>().Value;
        client.Timeout = TimeSpan.FromMilliseconds(Math.Max(500, opts.RequestTimeoutMs));
    });

builder.Services.AddSingleton<ILocalAdminClient, LocalAdminClient>();
builder.Services.AddSingleton<AdminFanOutService>();
builder.Services.AddOpenApi();

WebApplication app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(options =>
    {
        options.WithTitle("CacheOrchestrator Admin");
        options.WithDefaultHttpClient(ScalarTarget.CSharp, ScalarClient.HttpClient);
    });
}

app.UseDefaultFiles();
app.UseStaticFiles();

RouteGroupBuilder api = app.MapGroup("/api").WithTags("Admin App");

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

api.MapGet("/stats", async (
    string? scope,
    bool? groupByInstance,
    string? instances,
    AdminFanOutService fanOut,
    CancellationToken cancellationToken) =>
{
    try
    {
        ClusterStatsDto stats = await fanOut
            .GetStatsAsync(scope, cancellationToken, groupByInstance ?? false, instances)
            .ConfigureAwait(false);
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
    AdminAppInvalidateRequest body,
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
    AdminAppVersionRequest body,
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
    AdminAppTtlPatchRequest body,
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

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "CacheOrchestrator.Admin" }));

app.Run();

/// <summary>Exposes the entry assembly for WebApplicationFactory tests.</summary>
public partial class Program;
