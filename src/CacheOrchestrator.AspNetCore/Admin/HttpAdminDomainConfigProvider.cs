using CacheOrchestrator.Configuration;

namespace CacheOrchestrator.Admin;

internal sealed class HttpAdminDomainConfigProvider : IAdminDomainConfigProvider
{
    private readonly IRequestDomainCacheOptions _domainOptions;
    private readonly IDomainRuntimeOverrideStore _overrides;
    private readonly IHttpDomainRuntimeOverrideStore _httpOverrides;
    private readonly TimeProvider _time;

    public HttpAdminDomainConfigProvider(
        IRequestDomainCacheOptions domainOptions,
        IDomainRuntimeOverrideStore overrides,
        IHttpDomainRuntimeOverrideStore httpOverrides,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(domainOptions);
        ArgumentNullException.ThrowIfNull(overrides);
        ArgumentNullException.ThrowIfNull(httpOverrides);
        _domainOptions = domainOptions;
        _overrides = overrides;
        _httpOverrides = httpOverrides;
        _time = timeProvider ?? TimeProvider.System;
    }

    public AdminDomainConfigDto GetDomainConfig(string normalizedDomain)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedDomain);

        DomainHttpCacheOptions options = _domainOptions.GetOrCreateDomainOptions(normalizedDomain);
        DomainRuntimeOverride? runtimeOverride = _overrides.Get(normalizedDomain);
        HttpDomainRuntimeOverride? httpRuntimeOverride = _httpOverrides.Get(normalizedDomain);

        return new AdminDomainConfigDto
        {
            Name = normalizedDomain,
            Version = options.Version,
            VersionIsRuntimeOverride = runtimeOverride?.Version is not null,
            OutputCacheEnabled = options.OutputCacheEnabled,
            DataCacheEnabled = options.DataCacheEnabled,
            DataCacheInstanceName = options.DataCacheInstanceName,
            OutputCacheTtlSeconds = (int)options.OutputTtl.TotalSeconds,
            DataCacheTtlSeconds = (int)options.DataCacheTtl.TotalSeconds,
            ClientTtlSeconds = options.ClientTtlSeconds,
            ClientTtlMinSeconds = options.ClientTtlMinSeconds,
            ScheduledUpdateUtc = options.ScheduledUpdateUtc,
            SchedulePhase = ResolveSchedulePhase(options),
            RuntimeOverrides = runtimeOverride is null && httpRuntimeOverride is null
                ? null
                : new AdminRuntimeOverrideFlagsDto
                {
                    Version = runtimeOverride?.Version is not null,
                    OutputCacheTtl = httpRuntimeOverride?.OutputCacheTtl is not null,
                    DataCacheTtl = runtimeOverride?.DataCacheTtl is not null,
                    ClientTtl = httpRuntimeOverride?.ClientTtl is not null,
                    ClientTtlMin = httpRuntimeOverride?.ClientTtlMin is not null
                }
        };
    }

    private string? ResolveSchedulePhase(DomainHttpCacheOptions options)
    {
        if (options.ScheduledUpdateUtc is null)
            return null;

        ClientCacheHeaderGenerator.Result built = ClientCacheHeaderGenerator.Build(options, _time.GetUtcNow());
        string phase = CacheOrchestratorHeaderFormatter.PhaseToString(built.Phase);
        return phase == "n/a" ? null : phase;
    }
}
