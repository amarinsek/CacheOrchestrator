namespace CacheOrchestrator.FusionCache;

/// <summary>
/// Resolves Fusion-specific domain settings from configuration (and optional runtime overlays).
/// </summary>
public interface IFusionDomainSettingsProvider
{
    /// <summary>
    /// Effective Fusion engine settings for <paramref name="domain"/> (defaults + domain + overlay).
    /// </summary>
    DomainFusionCacheSettings Get(string domain);
}
