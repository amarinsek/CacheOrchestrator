using CacheOrchestrator.AdminConsole.Endpoints;
using CacheOrchestrator.AdminConsole.Options;
using CacheOrchestrator.AdminConsole.Services;
using CacheOrchestrator.AdminConsole.Services.Hints;
using CacheOrchestrator.AdminConsole.Services.Metrics;
using Scalar.AspNetCore;
using System.Text.Json.Serialization;

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

app.MapAdminConsoleApi();

app.Run();

/// <summary>Exposes the entry assembly for WebApplicationFactory tests.</summary>
public partial class Program;
