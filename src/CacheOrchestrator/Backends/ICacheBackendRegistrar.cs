namespace CacheOrchestrator.Backends;

/// <summary>
/// Registers storage for Output Cache and/or a named FusionCache instance.
/// </summary>
/// <remarks>
/// <para>
/// Implement this interface for custom providers (SQL Server L2, Memcached, Cosmos DB, …).
/// Register with <c>AddCacheOrchestrator(config, o =&gt; o.AddBackend(new MyRegistrar()))</c>.
/// The <see cref="Name"/> must match the <c>Provider</c> value in configuration.
/// </para>
/// <para>
/// <strong>Responsibilities:</strong>
/// </para>
/// <list type="table">
/// <listheader><term>Surface</term><description>What to implement</description></listheader>
/// <item>
/// <term>Output Cache</term>
/// <description>
/// If <see cref="SupportsOutputCacheStore"/> is <see langword="true"/>: configure options via
/// <see cref="OutputCacheRegistrationContext.Configure"/> and/or register a store with
/// <see cref="OutputCacheRegistrationContext.RegisterStore"/>. If <see langword="false"/>,
/// this provider must not be used as <c>OutputCache.Provider</c> (Fusion-only backends).
/// </description>
/// </item>
/// <item>
/// <term>FusionCache L2</term>
/// <description>
/// Register a <strong>keyed</strong> <c>IDistributedCache</c> (and optional backplane) for
/// <see cref="FusionCacheRegistrationContext.InstanceName"/>, then wire it with
/// <c>WithRegisteredKeyedDistributedCache(instanceName)</c>. Never use a single global
/// <c>AddStackExchangeRedisCache</c> / <c>AddDistributedSqlServerCache</c> for multi-instance.
/// </description>
/// </item>
/// <item>
/// <term>Health</term>
/// <description>Optional: register <c>ICacheOrchestratorHealthProbe</c> implementations.</description>
/// </item>
/// </list>
/// <para>
/// Bind backend-specific settings from <see cref="OutputCacheRegistrationContext.BackendSection"/> /
/// <see cref="FusionCacheRegistrationContext.BackendSection"/> (see <see cref="BackendConfiguration"/>).
/// </para>
/// </remarks>
public interface ICacheBackendRegistrar
{
    /// <summary>
    /// Backend name. Must match the value used in configuration (<c>Provider</c> property).
    /// </summary>
    string Name { get; }

    /// <summary>
    /// When <see langword="false"/>, this backend cannot be selected as <c>OutputCache.Provider</c>
    /// (Fusion L2 / health only). Default: <see langword="true"/>.
    /// </summary>
    bool SupportsOutputCacheStore => true;

    /// <summary>
    /// Configures Output Cache options and/or registers the Output Cache store for this backend.
    /// </summary>
    /// <param name="context">Registration context (services, config paths, option callbacks).</param>
    void RegisterOutputCache(OutputCacheRegistrationContext context);

    /// <summary>
    /// Registers distributed cache, optional backplane, and related services for one named FusionCache instance.
    /// </summary>
    /// <param name="context">Registration context for this instance.</param>
    void RegisterFusionCache(FusionCacheRegistrationContext context);

    /// <summary>
    /// Registers health probes for this backend (Fusion instance or Output Cache key <c>oc</c>).
    /// </summary>
    /// <param name="context">Health registration context.</param>
    void RegisterHealthProbes(BackendHealthRegistrationContext context);
}
