namespace CacheOrchestrator.Backends;

/// <summary>
/// Registers storage for Output Cache (and optional health probes).
/// </summary>
/// <remarks>
/// <para>
/// Implement this interface for custom Output Cache providers (SQL Server, Redis, …).
/// Register with <c>AddCacheOrchestrator(config, o =&gt; o.AddOutputCacheBackend(new MyRegistrar()))</c>.
/// The <see cref="Name"/> must match the <c>Provider</c> value in configuration.
/// </para>
/// <para>
/// FusionCache L2 / backplane registrars live in the FusionCache package
/// (<c>IFusionCacheBackendRegistrar</c>). Redis registers both surfaces via <c>AddRedisBackend</c>.
/// </para>
/// </remarks>
public interface IOutputCacheBackendRegistrar
{
    /// <summary>
    /// Backend name. Must match the value used in configuration (<c>Provider</c> property).
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Configures Output Cache options and/or registers the Output Cache store for this backend.
    /// </summary>
    /// <param name="context">Registration context (services, config paths, option callbacks).</param>
    void RegisterOutputCache(OutputCacheRegistrationContext context);

}

