using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace CacheOrchestrator.Configuration;

/// <summary>
/// HTTP request wrapper over <see cref="IDomainCacheOptionsProvider"/> that pins options on
/// <see cref="ICacheOrchestratorFeature"/>.
/// </summary>
internal sealed class RequestDomainCacheOptionsProvider : IRequestDomainCacheOptions
{
    private readonly IDomainCacheOptionsProvider _inner;
    private readonly ILogger<RequestDomainCacheOptionsProvider> _logger;

    public RequestDomainCacheOptionsProvider(
        IDomainCacheOptionsProvider inner,
        ILogger<RequestDomainCacheOptionsProvider> logger)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(logger);
        _inner = inner;
        _logger = logger;
    }

    /// <inheritdoc />
    public DomainCacheOptions GetOrCreateDomainOptions(string domain) =>
        _inner.GetOrCreateDomainOptions(domain);

    /// <inheritdoc />
    public DomainCacheOptions EnsureDomainOptions(HttpContext http, string domain)
    {
        ArgumentNullException.ThrowIfNull(http);

        string normalized = DomainName.Normalize(domain);
        ICacheOrchestratorFeature feature = CacheOrchestratorFeatureAccessor.GetOrCreate(http);

        if (feature.DomainOptions is { } cached)
        {
            if (string.Equals(cached.Domain, normalized, StringComparison.Ordinal))
                return cached;

            if (_logger.IsEnabled(LogLevel.Warning))
            {
                _logger.LogWarning(
                    "Replacing request domain snapshot '{PreviousDomain}' with '{Domain}'.",
                    cached.Domain,
                    normalized);
            }
        }

        DomainCacheOptions resolved = _inner.GetOrCreateDomainOptions(normalized);
        feature.DomainOptions = resolved;
        return resolved;
    }

    /// <inheritdoc />
    public DomainCacheOptions? GetDomainOptions(HttpContext http)
    {
        ArgumentNullException.ThrowIfNull(http);
        return http.Features.Get<ICacheOrchestratorFeature>()?.DomainOptions;
    }
}
