using CacheOrchestrator.Admin;
using CacheOrchestrator.AdminConsole.Models;
using CacheOrchestrator.AdminConsole.Options;
using CacheOrchestrator.Invalidation;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Text.Json;

namespace CacheOrchestrator.AdminConsole.Services;

/// <summary>
/// Default <see cref="IAdminApiClient"/> using <see cref="IHttpClientFactory"/>.
/// </summary>
public sealed class AdminApiClient : IAdminApiClient
{
    /// <summary>Named HttpClient registration key.</summary>
    public const string HttpClientName = "AdminConsoleAdminApi";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly AdminConsoleOptions _options;

    public AdminApiClient(IHttpClientFactory httpClientFactory, IOptions<AdminConsoleOptions> options)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(options);
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
    }

    /// <inheritdoc />
    public Task<InstanceCallOutcome<AdminHealthDto>> GetHealthAsync(
        AdminInstanceOptions instance,
        CancellationToken cancellationToken = default) =>
        GetAsync<AdminHealthDto>(instance, "/health", cancellationToken);

    /// <inheritdoc />
    public async Task<InstanceCallOutcome<IReadOnlyList<AdminDomainConfigDto>>> GetDomainsAsync(
        AdminInstanceOptions instance,
        CancellationToken cancellationToken = default)
    {
        InstanceCallOutcome<List<AdminDomainConfigDto>> raw =
            await GetAsync<List<AdminDomainConfigDto>>(instance, "/domains", cancellationToken)
                .ConfigureAwait(false);

        return new InstanceCallOutcome<IReadOnlyList<AdminDomainConfigDto>>
        {
            InstanceId = raw.InstanceId,
            Succeeded = raw.Succeeded,
            Value = raw.Value,
            StatusCode = raw.StatusCode,
            Error = raw.Error,
            LatencyMs = raw.LatencyMs
        };
    }

    /// <inheritdoc />
    public Task<InstanceCallOutcome<CacheInvalidationResult>> InvalidateAsync(
        AdminInstanceOptions instance,
        AdminInvalidateRequest body,
        CancellationToken cancellationToken = default) =>
        PostAsync<AdminInvalidateRequest, CacheInvalidationResult>(
            instance, "/invalidate", body, cancellationToken);

    /// <inheritdoc />
    public Task<InstanceCallOutcome<AdminDomainMutationResultDto>> SetVersionAsync(
        AdminInstanceOptions instance,
        string domain,
        AdminVersionRequest body,
        CancellationToken cancellationToken = default) =>
        PostAsync<AdminVersionRequest, AdminDomainMutationResultDto>(
            instance, $"/domains/{Uri.EscapeDataString(domain)}/version", body, cancellationToken);

    /// <inheritdoc />
    public Task<InstanceCallOutcome<AdminDomainMutationResultDto>> PatchSettingsAsync(
        AdminInstanceOptions instance,
        string domain,
        AdminSettingsPatchRequest body,
        CancellationToken cancellationToken = default) =>
        SendAsync<AdminSettingsPatchRequest, AdminDomainMutationResultDto>(
            instance,
            HttpMethod.Patch,
            $"/domains/{Uri.EscapeDataString(domain)}/settings",
            body,
            cancellationToken);

    /// <inheritdoc />
    public Task<InstanceCallOutcome<AdminDomainSettingsCatalogDto>> GetDomainSettingsCatalogAsync(
        AdminInstanceOptions instance,
        CancellationToken cancellationToken = default) =>
        GetAsync<AdminDomainSettingsCatalogDto>(instance, "/domain-settings/catalog", cancellationToken);

    /// <inheritdoc />
    public Task<InstanceCallOutcome<AdminApiClusterInfoDto>> GetClusterInfoAsync(
        AdminInstanceOptions instance,
        CancellationToken cancellationToken = default) =>
        GetAsync<AdminApiClusterInfoDto>(instance, "/cluster/info", cancellationToken);

    private async Task<InstanceCallOutcome<T>> GetAsync<T>(
        AdminInstanceOptions instance,
        string relativePath,
        CancellationToken cancellationToken)
    {
        return await SendAsync<object, T>(
            instance,
            HttpMethod.Get,
            relativePath,
            body: null,
            cancellationToken).ConfigureAwait(false);
    }

    private Task<InstanceCallOutcome<TResponse>> PostAsync<TRequest, TResponse>(
        AdminInstanceOptions instance,
        string relativePath,
        TRequest body,
        CancellationToken cancellationToken) =>
        SendAsync<TRequest, TResponse>(instance, HttpMethod.Post, relativePath, body, cancellationToken);

    private async Task<InstanceCallOutcome<TResponse>> SendAsync<TRequest, TResponse>(
        AdminInstanceOptions instance,
        HttpMethod method,
        string relativePath,
        TRequest? body,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(instance);
        if (string.IsNullOrWhiteSpace(instance.Id))
            throw new ArgumentException("Instance Id is required.", nameof(instance));
        if (string.IsNullOrWhiteSpace(instance.Url))
            throw new ArgumentException("Instance Url is required.", nameof(instance));

        string baseUrl = instance.Url.TrimEnd('/');
        string prefix = string.IsNullOrWhiteSpace(_options.AdminApiPathPrefix)
            ? "/cache-admin/local"
            : _options.AdminApiPathPrefix.TrimEnd('/');
        string url = baseUrl + prefix + relativePath;

        HttpClient client = _httpClientFactory.CreateClient(HttpClientName);
        using HttpRequestMessage request = new(method, url);
        if (!string.IsNullOrEmpty(_options.ApiKey))
            request.Headers.TryAddWithoutValidation("X-CacheOrchestrator-Admin-Key", _options.ApiKey);

        if (body is not null && method != HttpMethod.Get)
            request.Content = JsonContent.Create(body, options: JsonOptions);

        var sw = Stopwatch.StartNew();
        try
        {
            using HttpResponseMessage response = await client
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            sw.Stop();
            string raw = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                if ((int)response.StatusCode == StatusCodes.Status409Conflict
                    && TryParseClusterPublishIncomplete(raw, out AdminApiClusterPublishIncompleteDto? incomplete)
                    && incomplete is not null
                    && incomplete.LocalApplied
                    && incomplete.PeerFailures is { Count: > 0 })
                {
                    return new InstanceCallOutcome<TResponse>
                    {
                        InstanceId = instance.Id,
                        Succeeded = false,
                        StatusCode = StatusCodes.Status409Conflict,
                        Error = string.IsNullOrWhiteSpace(incomplete.Error)
                            ? "Cluster publish incomplete."
                            : incomplete.Error.Trim(),
                        LatencyMs = sw.Elapsed.TotalMilliseconds,
                        LocalApplied = true,
                        PeerFailures = incomplete.PeerFailures
                            .Where(p => !string.IsNullOrWhiteSpace(p.PeerId))
                            .ToArray(),
                    };
                }

                return Fail<TResponse>(
                    instance.Id,
                    (int)response.StatusCode,
                    FormatHttpError((int)response.StatusCode, response.ReasonPhrase, raw),
                    sw.Elapsed.TotalMilliseconds);
            }

            if (LooksLikeHtmlOrNonJson(raw, response.Content.Headers.ContentType?.MediaType))
            {
                return Fail<TResponse>(
                    instance.Id,
                    (int)response.StatusCode,
                    "Non-JSON response (often SPA MapFallbackToFile HTML). " +
                    "Endpoint may be missing — enable Admin API and/or map the cluster bus receive endpoints.",
                    sw.Elapsed.TotalMilliseconds);
            }

            TResponse? value;
            try
            {
                value = string.IsNullOrWhiteSpace(raw)
                    ? default
                    : JsonSerializer.Deserialize<TResponse>(raw, JsonOptions);
            }
            catch (JsonException ex)
            {
                return Fail<TResponse>(
                    instance.Id,
                    (int)response.StatusCode,
                    "Invalid JSON from instance: " + ex.Message,
                    sw.Elapsed.TotalMilliseconds);
            }

            return new InstanceCallOutcome<TResponse>
            {
                InstanceId = instance.Id,
                Succeeded = true,
                Value = value,
                StatusCode = (int)response.StatusCode,
                LatencyMs = sw.Elapsed.TotalMilliseconds
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            sw.Stop();
            return Fail<TResponse>(instance.Id, null, "Request timed out.", sw.Elapsed.TotalMilliseconds);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            sw.Stop();
            return Fail<TResponse>(instance.Id, null, ex.Message, sw.Elapsed.TotalMilliseconds);
        }
    }

    private static InstanceCallOutcome<T> Fail<T>(
        string instanceId,
        int? statusCode,
        string error,
        double latencyMs) =>
        new()
        {
            InstanceId = instanceId,
            Succeeded = false,
            StatusCode = statusCode,
            Error = error,
            LatencyMs = latencyMs
        };

    private static bool LooksLikeHtmlOrNonJson(string raw, string? mediaType)
    {
        if (!string.IsNullOrEmpty(mediaType)
            && mediaType.Contains("json", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        ReadOnlySpan<char> trimmed = raw.AsSpan().TrimStart();
        if (trimmed.IsEmpty)
            return false;

        if (trimmed[0] is '<' or '!')
            return true;

        if (!string.IsNullOrEmpty(mediaType)
            && (mediaType.Contains("html", StringComparison.OrdinalIgnoreCase)
                || mediaType.Contains("text/plain", StringComparison.OrdinalIgnoreCase))
            && trimmed[0] is not '{' and not '[')
        {
            return true;
        }

        return false;
    }

    private static string FormatHttpError(int statusCode, string? reason, string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return $"HTTP {statusCode} {reason}".Trim();

        string trimmed = body.Trim();
        if (trimmed.StartsWith('<') || trimmed.StartsWith("<!"))
            return $"HTTP {statusCode}: non-JSON body (HTML). Check Admin API path and that the target is not an SPA fallback.";

        if (TryParseClusterPublishIncomplete(trimmed, out AdminApiClusterPublishIncompleteDto? incomplete)
            && incomplete is not null
            && !string.IsNullOrWhiteSpace(incomplete.Error))
        {
            return incomplete.Error.Trim();
        }

        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("error", out JsonElement err)
                && err.ValueKind == JsonValueKind.String)
            {
                string? msg = err.GetString();
                if (!string.IsNullOrWhiteSpace(msg))
                    return msg.Trim();
            }
        }
        catch (JsonException)
        {
            // fall through to raw body
        }

        if (trimmed.Length > 280)
            trimmed = trimmed[..280] + "…";
        return trimmed;
    }

    private static bool TryParseClusterPublishIncomplete(
        string raw,
        out AdminApiClusterPublishIncompleteDto? dto)
    {
        dto = null;
        if (string.IsNullOrWhiteSpace(raw))
            return false;
        try
        {
            dto = JsonSerializer.Deserialize<AdminApiClusterPublishIncompleteDto>(raw, JsonOptions);
            return dto is not null
                && (dto.LocalApplied || (dto.PeerFailures is { Count: > 0 }));
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
