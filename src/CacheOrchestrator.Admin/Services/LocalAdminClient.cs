using CacheOrchestrator.Admin;
using CacheOrchestrator.Admin.App.Models;
using CacheOrchestrator.Admin.App.Options;
using CacheOrchestrator.Invalidation;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;

namespace CacheOrchestrator.Admin.App.Services;

/// <summary>
/// Default <see cref="ILocalAdminClient"/> using <see cref="IHttpClientFactory"/>.
/// </summary>
public sealed class LocalAdminClient : ILocalAdminClient
{
    /// <summary>Named HttpClient registration key.</summary>
    public const string HttpClientName = "CacheAdminLocal";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly CacheAdminOptions _options;

    public LocalAdminClient(IHttpClientFactory httpClientFactory, IOptions<CacheAdminOptions> options)
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
    public Task<InstanceCallOutcome<AdminLiveStatsSnapshot>> GetStatsAsync(
        AdminInstanceOptions instance,
        CancellationToken cancellationToken = default) =>
        GetAsync<AdminLiveStatsSnapshot>(instance, "/stats", cancellationToken);

    /// <inheritdoc />
    public async Task<InstanceCallOutcome<IReadOnlyList<AdminEndpointInfoDto>>> GetEndpointsAsync(
        AdminInstanceOptions instance,
        CancellationToken cancellationToken = default)
    {
        InstanceCallOutcome<List<AdminEndpointInfoDto>> raw =
            await GetAsync<List<AdminEndpointInfoDto>>(instance, "/endpoints", cancellationToken)
                .ConfigureAwait(false);

        return new InstanceCallOutcome<IReadOnlyList<AdminEndpointInfoDto>>
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
    public Task<InstanceCallOutcome<AdminDomainMutationResultDto>> PatchTtlAsync(
        AdminInstanceOptions instance,
        string domain,
        AdminTtlPatchRequest body,
        CancellationToken cancellationToken = default) =>
        SendAsync<AdminTtlPatchRequest, AdminDomainMutationResultDto>(
            instance,
            HttpMethod.Patch,
            $"/domains/{Uri.EscapeDataString(domain)}/ttl",
            body,
            cancellationToken);

    /// <inheritdoc />
    public Task<InstanceCallOutcome<LocalClusterInfoDto>> GetClusterInfoAsync(
        AdminInstanceOptions instance,
        CancellationToken cancellationToken = default) =>
        GetAsync<LocalClusterInfoDto>(instance, "/cluster/info", cancellationToken);

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
        string prefix = string.IsNullOrWhiteSpace(_options.LocalPathPrefix)
            ? "/cache-admin/local"
            : _options.LocalPathPrefix.TrimEnd('/');
        string url = baseUrl + prefix + relativePath;

        HttpClient client = _httpClientFactory.CreateClient(HttpClientName);
        using HttpRequestMessage request = new(method, url);
        if (!string.IsNullOrEmpty(_options.ApiKey))
            request.Headers.TryAddWithoutValidation("X-Cache-Admin-Key", _options.ApiKey);

        if (body is not null && method != HttpMethod.Get)
            request.Content = JsonContent.Create(body, options: JsonOptions);

        Stopwatch sw = Stopwatch.StartNew();
        try
        {
            using HttpResponseMessage response = await client
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            sw.Stop();
            if (!response.IsSuccessStatusCode)
            {
                string errBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                return Fail<TResponse>(
                    instance.Id,
                    (int)response.StatusCode,
                    string.IsNullOrWhiteSpace(errBody)
                        ? $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}"
                        : errBody,
                    sw.Elapsed.TotalMilliseconds);
            }

            TResponse? value = await response.Content
                .ReadFromJsonAsync<TResponse>(JsonOptions, cancellationToken)
                .ConfigureAwait(false);

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
}
