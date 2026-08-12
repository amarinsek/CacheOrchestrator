using CacheOrchestrator.Admin;
using CacheOrchestrator.Backends;
using CacheOrchestrator.Cluster;
using CacheOrchestrator.Configuration;
using CacheOrchestrator.FusionCache;
using CacheOrchestrator.Invalidation;
using CacheOrchestrator.OutputCache;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using ZiggyCreatures.Caching.Fusion;

namespace CacheOrchestrator.DependencyInjection;

/// <summary>
/// DI registration entry points for CacheOrchestrator.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers CacheOrchestrator services, options validation, Output Cache, and all named
    /// FusionCache instances defined in <c>FusionCacheInstances</c>.
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
    public static IServiceCollection AddCacheOrchestrator(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<ICacheOrchestratorBuilder>? configure = null,
        string configSection = "Cache",
        bool enableMvcConvention = true)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

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

        // Register each named FusionCache instance
        services.AddMemoryCache();
        foreach ((string? instanceName, CacheOrchestratorOptions.FusionCacheInstanceOptions? instanceOptions) in opts.FusionCacheInstances)
        {
            ICacheBackendRegistrar fusionRegistrar = builder.ResolveRegistrar(instanceOptions.Provider);
            RegisterFusionCacheInstance(
                services, configuration, opts, configSection, instanceName, instanceOptions, fusionRegistrar);

            fusionRegistrar.RegisterHealthProbes(new BackendHealthRegistrationContext(
                services,
                configuration,
                configSection,
                instanceName,
                fusionRegistrar.Name,
                opts,
                instanceOptions));
        }

        outputRegistrar.RegisterHealthProbes(new BackendHealthRegistrationContext(
            services,
            configuration,
            configSection,
            instanceName: "oc",
            providerName: outputRegistrar.Name,
            rootOptions: opts,
            instanceOptions: new CacheOrchestratorOptions.FusionCacheInstanceOptions()));

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

        services.AddSingleton<IValidateOptions<CacheOrchestratorOptions>>(
            new CacheOrchestratorOptionsValidator(validProviders, outputCacheSupport));

        CacheOrchestratorOptions opts = new();
        configuration.GetSection(configSection).Bind(opts);
        return opts;
    }

    private static void RegisterCoreServices(IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IInstanceIdProvider, DefaultInstanceIdProvider>();
        services.TryAddSingleton<ClusterCommandFactory>();
        // Bus package may register real implementations in the builder callback before this runs;
        // TryAdd keeps Http/Static bus when already present.
        services.TryAddSingleton<IClusterCommandBus>(_ => NullClusterCommandBus.Instance);
        services.TryAddSingleton<IClusterMembership>(_ => NullClusterMembership.Instance);
        services.TryAddSingleton<IClusterCommandHandler, DefaultClusterCommandHandler>();
        services.AddSingleton<IDomainCacheOptionsProvider, DomainCacheOptionsProvider>();
        services.AddSingleton<IDomainFusionCache, DomainFusionCacheService>();
        services.AddSingleton<ICacheOrchestratorInvalidator, CacheOrchestratorInvalidator>();
        services.TryAddSingleton<IDomainKeyGenerator, DefaultDomainKeyGenerator>();
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
                $"and keep '{registrar.Name}' only under FusionCacheInstances.");
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

    private static void RegisterFusionCacheInstance(
        IServiceCollection services,
        IConfiguration configuration,
        CacheOrchestratorOptions rootOpts,
        string configSection,
        string instanceName,
        CacheOrchestratorOptions.FusionCacheInstanceOptions instanceOpts,
        ICacheBackendRegistrar registrar)
    {
        DistributedResilienceOptions resilience = rootOpts.GetEffectiveDistributedResilience();
        bool isDistributed = !string.Equals(instanceOpts.Provider, "InMemory", StringComparison.OrdinalIgnoreCase);

        // L2 is NOT wired here. Each backend registrar attaches its own distributed cache
        // (Redis uses a keyed IDistributedCache per instance name so multi-cluster works).
        IFusionCacheBuilder fusionBuilder = services
            .AddFusionCache(instanceName)
            .WithOptions(o =>
            {
                if (isDistributed)
                {
                    o.DistributedCacheCircuitBreakerDuration =
                        TimeSpan.FromSeconds(Math.Max(0, resilience.CircuitBreakerSeconds));
                    o.DefaultEntryOptions.DistributedCacheSoftTimeout =
                        TimeSpan.FromSeconds(Math.Max(0, resilience.SoftTimeoutSeconds));
                    o.DefaultEntryOptions.DistributedCacheHardTimeout =
                        TimeSpan.FromSeconds(Math.Max(0, resilience.HardTimeoutSeconds));
                    o.DefaultEntryOptions.AllowBackgroundDistributedCacheOperations = true;
                }
            })
            .WithSystemTextJsonSerializer()
            .TryWithRegisteredMemoryCache();

        FusionCacheRegistrationContext context = new(
            services,
            configuration,
            rootOpts,
            configSection,
            instanceName,
            instanceOpts,
            registrar.Name,
            fusionBuilder,
            resilience);

        registrar.RegisterFusionCache(context);
    }
}
