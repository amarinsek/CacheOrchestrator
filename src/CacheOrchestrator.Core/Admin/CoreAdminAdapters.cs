using CacheOrchestrator.Configuration;

namespace CacheOrchestrator.Admin;

internal sealed class NullAdminEndpointCatalog : IAdminEndpointCatalog
{
    public static readonly NullAdminEndpointCatalog Instance = new();

    private NullAdminEndpointCatalog()
    {
    }

    public IReadOnlyList<AdminEndpointInfoDto> GetEndpoints() => [];
}

internal sealed class CoreAdminDomainConfigProvider : IAdminDomainConfigProvider
{
    private readonly IDomainCacheOptionsProvider _domainOptions;
    private readonly IDomainRuntimeOverrideStore _overrides;

    public CoreAdminDomainConfigProvider(
        IDomainCacheOptionsProvider domainOptions,
        IDomainRuntimeOverrideStore overrides)
    {
        ArgumentNullException.ThrowIfNull(domainOptions);
        ArgumentNullException.ThrowIfNull(overrides);
        _domainOptions = domainOptions;
        _overrides = overrides;
    }

    public AdminDomainConfigDto GetDomainConfig(string normalizedDomain)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedDomain);

        DomainCacheOptions options = _domainOptions.GetOrCreateDomainOptions(normalizedDomain);
        DomainRuntimeOverride? runtimeOverride = _overrides.Get(normalizedDomain);

        return new AdminDomainConfigDto
        {
            Name = normalizedDomain,
            Version = options.Version,
            VersionIsRuntimeOverride = runtimeOverride?.Version is not null,
            DataCacheEnabled = options.DataCacheEnabled,
            DataCacheInstanceName = options.DataCacheInstanceName,
            DataCacheTtlSeconds = (int)options.DataCacheTtl.TotalSeconds,
            RuntimeOverrides = runtimeOverride is null
                ? null
                : new AdminRuntimeOverrideFlagsDto
                {
                    Version = runtimeOverride.Version is not null,
                    DataCacheTtl = runtimeOverride.DataCacheTtl is not null
                }
        };
    }
}
