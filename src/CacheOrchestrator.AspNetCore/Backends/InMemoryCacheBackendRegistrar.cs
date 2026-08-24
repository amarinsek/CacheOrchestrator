namespace CacheOrchestrator.Backends;

/// <summary>
/// Registers the in-process memory Output Cache store.
/// </summary>
public sealed class InMemoryCacheBackendRegistrar : ICacheBackendRegistrar
{
    /// <inheritdoc />
    public string Name => "InMemory";

    /// <inheritdoc />
    public bool SupportsOutputCacheStore => true;

    /// <inheritdoc />
    public void RegisterOutputCache(OutputCacheRegistrationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // In-memory Output Cache store is the ASP.NET default; set process-local size caps.
        context.Configure(options =>
        {
            options.SizeLimit = 512 * 1024 * 1024;
            options.MaximumBodySize = 32 * 1024 * 1024;
        });
    }

    /// <inheritdoc />
    public void RegisterHealthProbes(BackendHealthRegistrationContext context)
    {
        // In-process memory has no external dependency to probe.
    }
}
