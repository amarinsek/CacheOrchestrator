using CacheOrchestrator.Admin;
using CacheOrchestrator.Backends;
using CacheOrchestrator.Cluster;
using CacheOrchestrator.Configuration;
using CacheOrchestrator.DataCache;
using CacheOrchestrator.Invalidation;
using CacheOrchestrator.Orchestration;
using CacheOrchestrator.OutputCache;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace CacheOrchestrator.DependencyInjection;

/// <summary>
/// DI registration entry points for CacheOrchestrator ASP.NET Core host services.
/// </summary>
/// <remarks>
/// Registers Output Cache, Core orchestration, and <see cref="IDomainDataCache"/>.
/// Does <strong>not</strong> register a data-cache engine — call
/// <c>AddCacheOrchestratorFusionCache</c> or <c>AddCacheOrchestratorHybridCache</c> (or use the meta package).
/// </remarks>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers CacheOrchestrator ASP.NET services, options validation, and Output Cache.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Application configuration (binds the cache section).</param>
    /// <param name="configure">
    /// Optional builder callback (e.g. <c>o =&gt; o.AddRedisBackend()</c>,
    /// <c>o.ConfigureOutputCache(...)</c>, <c>o.AddBackend(custom)</c>).
    /// </param>
    /// <param name="configSection">Configuration section name. Default: <c>Cache</c>.</param>
    /// <param name="enableMvcConvention">
    /// When <see langword="true"/> (default), registers an MVC convention that automatically applies
    /// <c>[CacheDomain]</c> attributes on controllers as Output Cache policies.
    /// Set to <see langword="false"/> for Minimal API-only applications to avoid pulling in the full
    /// MVC infrastructure via <c>AddControllers()</c>.
    /// </param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    /// <remarks>
    /// Meta package <c>CacheOrchestrator</c> exposes <c>AddCacheOrchestrator</c> as AspNet + Fusion.
    /// Prefer this method for AspNet-only / Hybrid hosts, then call <c>AddCacheOrchestratorHybridCache</c>.
    /// </remarks>
    public static IServiceCollection AddCacheOrchestratorAspNetCore(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<ICacheOrchestratorBuilder>? configure = null,
        string configSection = "Cache",
        bool enableMvcConvention = true)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // Backend registrars and options monitors resolve IConfiguration from DI.
        // Host builders usually register it; bare ServiceCollection unit tests do not.
        services.TryAddSingleton(configuration);

        DefaultCacheOrchestratorBuilder builder = new(services, configuration);

        // Built-in InMemory only. Redis: install CacheOrchestrator.Redis and call AddRedisBackend().
        builder.AddBackend(new InMemoryCacheBackendRegistrar());

        // Allow consumers to add/override providers (e.g. o => o.AddRedisBackend())
        configure?.Invoke(builder);

        CacheOrchestratorOptions opts = BindAndValidateOptions(services, configuration, configSection, builder);

        RegisterCoreServices(services);
        RegisterAdminServices(services, opts.Admin);

        if (enableMvcConvention)
            RegisterControllerConvention(services);

        // Register Output Cache (single provider)
        ICacheBackendRegistrar outputRegistrar = builder.ResolveRegistrar(opts.OutputCache.Provider);
        RegisterOutputCache(services, configuration, opts, configSection, outputRegistrar, builder);

        outputRegistrar.RegisterHealthProbes(new BackendHealthRegistrationContext(
            services,
            configuration,
            configSection,
            instanceName: "oc",
            providerName: outputRegistrar.Name,
            rootOptions: opts,
            instanceOptions: new CacheOrchestratorOptions.DataCacheInstanceOptions()));

        return services;
    }

    private static CacheOrchestratorOptions BindAndValidateOptions(
        IServiceCollection services,
        IConfiguration configuration,
        string configSection,
        DefaultCacheOrchestratorBuilder builder)
    {
        services
            .AddOptions<CacheOrchestratorOptions>()
            .Bind(configuration.GetSection(configSection))
            .ValidateOnStart();

        HashSet<string> validProviders = new(builder.GetRegisteredProviderNames(), StringComparer.OrdinalIgnoreCase);
        Dictionary<string, bool> outputCacheSupport = new(StringComparer.OrdinalIgnoreCase);
        foreach (string name in builder.GetRegisteredProviderNames())
        {
            ICacheBackendRegistrar reg = builder.ResolveRegistrar(name);
            outputCacheSupport[name] = reg.SupportsOutputCacheStore;
        }

        services.AddSingleton<IValidateOptions<CacheOrchestratorOptions>>(sp =>
            new CacheOrchestratorOptionsValidator(
                validProviders,
                outputCacheSupport,
                sp.GetService<Microsoft.Extensions.Logging.ILoggerFactory>()
                    ?.CreateLogger(typeof(CacheOrchestratorOptionsValidator).FullName
                        ?? nameof(CacheOrchestratorOptionsValidator))));

        CacheOrchestratorOptions opts = new();
        configuration.GetSection(configSection).Bind(opts);
        return opts;
    }

    private static void RegisterCoreServices(IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IInstanceIdProvider, DefaultInstanceIdProvider>();
        services.TryAddSingleton<ClusterCommandFactory>();
        services.TryAddSingleton<ClusterCommandDedupeStore>();
        // Bus package may register real implementations in the builder callback before this runs;
        // TryAdd keeps Http/Static bus when already present.
        services.TryAddSingleton<IClusterCommandBus>(_ => NullClusterCommandBus.Instance);
        services.TryAddSingleton<IClusterMembership>(_ => NullClusterMembership.Instance);
        services.TryAddSingleton<IClusterCommandHandler, DefaultClusterCommandHandler>();
        services.AddSingleton<IDomainCacheOptionsProvider, DomainCacheOptionsProvider>();
        services.AddSingleton<IRequestDomainCacheOptions, RequestDomainCacheOptionsProvider>();
        services.AddSingleton<IDomainDataCache, DomainDataCacheService>();
        services.AddSingleton<ICacheOrchestrator, CacheOrchestratorService>();
        services.TryAddSingleton<IHttpCacheInvalidationSink, OutputCacheInvalidationSink>();
        services.AddSingleton<ICacheOrchestratorInvalidator, CacheOrchestratorInvalidator>();
        services.TryAddSingleton<Vary.CacheVaryMaterializer>(sp =>
            new Vary.CacheVaryMaterializer(sp.GetServices<Vary.ICacheVaryContributor>()));
        services.TryAddSingleton<IDomainKeyGenerator>(sp =>
            new DefaultDomainKeyGenerator(sp.GetRequiredService<Vary.CacheVaryMaterializer>()));
    }

    private static void RegisterAdminServices(
        IServiceCollection services,
        CacheOrchestratorOptions.AdminOptions admin)
    {
        if (admin.Enabled)
        {
            services.AddSingleton<IDomainRuntimeOverrideStore, DomainRuntimeOverrideStore>();
            services.AddSingleton<IAdminStatsCollector>(sp =>
            {
                CacheOrchestratorOptions opts = sp.GetRequiredService<IOptions<CacheOrchestratorOptions>>().Value;
                string instanceId = sp.GetRequiredService<IInstanceIdProvider>().InstanceId;
                return new InMemoryAdminStatsCollector(
                    opts.Admin,
                    instanceId,
                    sp.GetService<TimeProvider>());
            });
            services.AddSingleton<IAdminEndpointCatalog, AdminEndpointCatalog>();
            services.AddSingleton<AdminQueryService>();
            services.AddSingleton<AdminApiKeyEndpointFilter>();
        }
        else
        {
            services.TryAddSingleton<IDomainRuntimeOverrideStore>(_ => NullDomainRuntimeOverrideStore.Instance);
            services.TryAddSingleton<IAdminStatsCollector>(_ => NoOpAdminStatsCollector.Instance);
            services.TryAddSingleton<IAdminEndpointCatalog>(_ => NullAdminEndpointCatalog.Instance);
        }
    }

    private static void RegisterControllerConvention(IServiceCollection services) =>
        services.AddControllers(options => options.Conventions.Add(new CacheDomainConvention()));

    private static void RegisterOutputCache(
        IServiceCollection services,
        IConfiguration configuration,
        CacheOrchestratorOptions opts,
        string configSection,
        ICacheBackendRegistrar registrar,
        DefaultCacheOrchestratorBuilder builder)
    {
        if (!registrar.SupportsOutputCacheStore)
        {
            throw new InvalidOperationException(
                $"OutputCache.Provider is '{registrar.Name}', but that backend does not support an Output Cache store " +
                $"(SupportsOutputCacheStore = false). Use a provider that supports Output Cache " +
                $"(e.g. InMemory, or Redis via CacheOrchestrator.Redis), " +
                $"and keep '{registrar.Name}' only under DataCacheInstances.");
        }

        List<Action<OutputCacheOptions>> optionConfigurators = [];

        // Base policy first (always).
        optionConfigurators.Add(o => o.AddBasePolicy(b => b.SetVaryByHeader(HeaderNames.AcceptEncoding)));

        OutputCacheRegistrationContext context = new(
            services,
            configuration,
            opts,
            configSection,
            registrar.Name,
            optionConfigurators);

        // Backend defaults (e.g. InMemory size limits) then user ConfigureOutputCache callbacks.
        registrar.RegisterOutputCache(context);
        foreach (Action<OutputCacheOptions> user in builder.OutputCacheConfigurators)
            optionConfigurators.Add(user);

        services.AddOutputCache(options =>
        {
            foreach (Action<OutputCacheOptions> configure in optionConfigurators)
                configure(options);
        });

        context.RunStoreRegistrations();
    }
}
