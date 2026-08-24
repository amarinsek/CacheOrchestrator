namespace CacheOrchestrator.Vary;

/// <summary>
/// Optional app-provided vary dimension. Register with
/// <c>services.AddSingleton&lt;ICacheVaryContributor, T&gt;()</c>.
/// </summary>
/// <remarks>
/// Contributors run after built-in domain settings. Prefer this over replacing
/// <see cref="DataCache.IDomainKeyGenerator"/> for small tenant/claim dimensions.
/// Hash secrets before <see cref="ICacheVaryBuilder.AddValue"/>; use <see cref="ICacheVaryBuilder.AddHashedValue"/> for raw secrets.
/// </remarks>
public interface ICacheVaryContributor
{
    /// <summary>
    /// Order: lower runs first. Built-in materializer logic is effectively 0;
    /// app contributors typically use 100+.
    /// </summary>
    int Order => 100;

    /// <summary>Contribute additional vary headers/values for the current request.</summary>
    /// <param name="context">Request and domain options.</param>
    /// <param name="builder">Vary accumulator (do not store secrets in plaintext).</param>
    void Contribute(CacheVaryContext context, ICacheVaryBuilder builder);
}
