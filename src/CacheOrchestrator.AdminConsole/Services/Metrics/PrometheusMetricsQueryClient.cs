using CacheOrchestrator.AdminConsole.Models;
using CacheOrchestrator.AdminConsole.Options;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;

namespace CacheOrchestrator.AdminConsole.Services.Metrics;

/// <summary>
/// Prometheus HTTP API v1 client (<c>/api/v1/query</c>, <c>/api/v1/query_range</c>, readiness).
/// Compatible with Prometheus, VictoriaMetrics, Mimir, and Thanos Query.
/// </summary>
public sealed class PrometheusMetricsQueryClient : IMetricsQueryClient
{
    /// <summary>Named <see cref="HttpClient"/> registration key.</summary>
    public const string HttpClientName = "AdminConsoleMetrics";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly AdminConsoleOptions _options;

    public PrometheusMetricsQueryClient(
        IHttpClientFactory httpClientFactory,
        IOptions<AdminConsoleOptions> options)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(options);
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
    }

    /// <inheritdoc />
    public async Task<MetricsProbeResult> ProbeAsync(CancellationToken cancellationToken = default)
    {
        MetricsStoreOptions metrics = _options.Metrics;
        if (!metrics.IsConfigured)
        {
            return new MetricsProbeResult
            {
                Succeeded = false,
                Error = "Metrics store is not configured.",
            };
        }

        if (!IsPrometheusProvider(metrics.Provider))
        {
            return new MetricsProbeResult
            {
                Succeeded = false,
                Error = $"Unsupported metrics provider '{metrics.Provider}'. Use Prometheus.",
            };
        }

        HttpClient client = CreateClient();
        var sw = Stopwatch.StartNew();
        try
        {
            // Prefer Prometheus readiness; fall back to buildinfo for some proxies.
            using HttpResponseMessage ready = await client
                .GetAsync(CombinePath(metrics.PathPrefix, "/-/ready"), cancellationToken)
                .ConfigureAwait(false);
            if (ready.IsSuccessStatusCode)
            {
                sw.Stop();
                return new MetricsProbeResult { Succeeded = true, LatencyMs = sw.Elapsed.TotalMilliseconds };
            }

            using HttpResponseMessage build = await client
                .GetAsync(CombinePath(metrics.PathPrefix, "/api/v1/status/buildinfo"), cancellationToken)
                .ConfigureAwait(false);
            sw.Stop();
            if (build.IsSuccessStatusCode)
            {
                return new MetricsProbeResult { Succeeded = true, LatencyMs = sw.Elapsed.TotalMilliseconds };
            }

            string body = await build.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            string snippet = Truncate(body, 200);
            return new MetricsProbeResult
            {
                Succeeded = false,
                LatencyMs = sw.Elapsed.TotalMilliseconds,
                Error = $"Probe failed HTTP {(int)build.StatusCode}: {snippet}",
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            sw.Stop();
            return new MetricsProbeResult
            {
                Succeeded = false,
                LatencyMs = sw.Elapsed.TotalMilliseconds,
                Error = "Metrics store probe timed out.",
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException)
        {
            sw.Stop();
            return new MetricsProbeResult
            {
                Succeeded = false,
                LatencyMs = sw.Elapsed.TotalMilliseconds,
                Error = ex.Message,
            };
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PrometheusMatrixSeries>> QueryRangeAsync(
        string promQl,
        DateTimeOffset startUtc,
        DateTimeOffset endUtc,
        string step,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        ArgumentException.ThrowIfNullOrWhiteSpace(promQl);
        ArgumentException.ThrowIfNullOrWhiteSpace(step);

        MetricsStoreOptions metrics = _options.Metrics;
        HttpClient client = CreateClient();
        string path = CombinePath(metrics.PathPrefix, "/api/v1/query_range");
        var query = new Dictionary<string, string>
        {
            ["query"] = promQl,
            ["start"] = startUtc.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
            ["end"] = endUtc.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
            ["step"] = step,
        };

        using HttpResponseMessage response = await client
            .PostAsync(path, new FormUrlEncodedContent(query), cancellationToken)
            .ConfigureAwait(false);
        string json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Prometheus query_range HTTP {(int)response.StatusCode}: {Truncate(json, 300)}");
        }

        return ParseMatrix(json);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PrometheusInstantSample>> QueryInstantAsync(
        string promQl,
        DateTimeOffset? timeUtc = null,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        ArgumentException.ThrowIfNullOrWhiteSpace(promQl);

        MetricsStoreOptions metrics = _options.Metrics;
        HttpClient client = CreateClient();
        string path = CombinePath(metrics.PathPrefix, "/api/v1/query");
        var form = new Dictionary<string, string> { ["query"] = promQl };
        if (timeUtc is { } t)
            form["time"] = t.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);

        using HttpResponseMessage response = await client
            .PostAsync(path, new FormUrlEncodedContent(form), cancellationToken)
            .ConfigureAwait(false);
        string json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Prometheus query HTTP {(int)response.StatusCode}: {Truncate(json, 300)}");
        }

        return ParseVector(json);
    }

    private void EnsureConfigured()
    {
        MetricsStoreOptions metrics = _options.Metrics;
        if (!metrics.IsConfigured)
            throw new InvalidOperationException("Metrics store is not configured.");
        if (!IsPrometheusProvider(metrics.Provider))
            throw new InvalidOperationException($"Unsupported metrics provider '{metrics.Provider}'.");
    }

    private HttpClient CreateClient()
    {
        HttpClient client = _httpClientFactory.CreateClient(HttpClientName);
        MetricsStoreOptions metrics = _options.Metrics;
        if (!string.IsNullOrWhiteSpace(metrics.BearerToken))
        {
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", metrics.BearerToken.Trim());
        }

        return client;
    }

    /// <summary>True when provider is empty or Prometheus (case-insensitive).</summary>
    public static bool IsPrometheusProvider(string? provider) =>
        string.IsNullOrWhiteSpace(provider)
        || string.Equals(provider.Trim(), "Prometheus", StringComparison.OrdinalIgnoreCase);

    /// <summary>Joins optional reverse-proxy prefix with a Prometheus API path.</summary>
    public static string CombinePath(string? pathPrefix, string apiPath)
    {
        string prefix = (pathPrefix ?? "").Trim().TrimEnd('/');
        if (prefix.Length == 0)
            return apiPath;
        if (!prefix.StartsWith('/'))
            prefix = "/" + prefix;
        return prefix + apiPath;
    }

    private static IReadOnlyList<PrometheusMatrixSeries> ParseMatrix(string json)
    {
        using var doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;
        EnsurePrometheusSuccess(root);

        if (!root.TryGetProperty("data", out JsonElement data)
            || !data.TryGetProperty("result", out JsonElement result)
            || result.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        List<PrometheusMatrixSeries> list = [];
        foreach (JsonElement series in result.EnumerateArray())
        {
            Dictionary<string, string> metric = ReadMetric(series);
            List<MetricsPointDto> points = [];
            if (series.TryGetProperty("values", out JsonElement values) && values.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement pair in values.EnumerateArray())
                {
                    if (TryReadSamplePair(pair, out long ts, out double val))
                        points.Add(new MetricsPointDto { T = ts, V = val });
                }
            }

            list.Add(new PrometheusMatrixSeries { Metric = metric, Points = points });
        }

        return list;
    }

    private static IReadOnlyList<PrometheusInstantSample> ParseVector(string json)
    {
        using var doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;
        EnsurePrometheusSuccess(root);

        if (!root.TryGetProperty("data", out JsonElement data)
            || !data.TryGetProperty("result", out JsonElement result)
            || result.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        List<PrometheusInstantSample> list = [];
        foreach (JsonElement sample in result.EnumerateArray())
        {
            Dictionary<string, string> metric = ReadMetric(sample);
            double? value = null;
            long? ts = null;
            if (sample.TryGetProperty("value", out JsonElement pair) && TryReadSamplePair(pair, out long t, out double v))
            {
                ts = t;
                value = v;
            }

            list.Add(new PrometheusInstantSample { Metric = metric, Value = value, TimestampUnix = ts });
        }

        return list;
    }

    private static void EnsurePrometheusSuccess(JsonElement root)
    {
        if (root.TryGetProperty("status", out JsonElement status)
            && status.ValueKind == JsonValueKind.String
            && !string.Equals(status.GetString(), "success", StringComparison.OrdinalIgnoreCase))
        {
            string err = root.TryGetProperty("error", out JsonElement e) ? e.GetString() ?? "error" : "error";
            throw new InvalidOperationException("Prometheus API error: " + err);
        }
    }

    private static Dictionary<string, string> ReadMetric(JsonElement series)
    {
        Dictionary<string, string> metric = new(StringComparer.Ordinal);
        if (!series.TryGetProperty("metric", out JsonElement m) || m.ValueKind != JsonValueKind.Object)
            return metric;
        foreach (JsonProperty p in m.EnumerateObject())
            metric[p.Name] = p.Value.GetString() ?? "";
        return metric;
    }

    private static bool TryReadSamplePair(JsonElement pair, out long unixSeconds, out double value)
    {
        unixSeconds = 0;
        value = 0;
        if (pair.ValueKind != JsonValueKind.Array)
            return false;
        int i = 0;
        JsonElement? tsEl = null;
        JsonElement? valEl = null;
        foreach (JsonElement el in pair.EnumerateArray())
        {
            if (i == 0)
                tsEl = el;
            else if (i == 1)
                valEl = el;
            i++;
        }

        if (tsEl is null || valEl is null)
            return false;

        unixSeconds = tsEl.Value.ValueKind switch
        {
            JsonValueKind.Number => (long)tsEl.Value.GetDouble(),
            JsonValueKind.String when double.TryParse(
                tsEl.Value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double ts) =>
                (long)ts,
            _ => 0,
        };

        string? raw = valEl.Value.ValueKind == JsonValueKind.String
            ? valEl.Value.GetString()
            : valEl.Value.GetRawText();
        if (string.IsNullOrEmpty(raw) || raw is "NaN" or "+Inf" or "-Inf")
            return false;
        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            return false;
        if (double.IsNaN(value) || double.IsInfinity(value))
            return false;
        return true;
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "…";
}
