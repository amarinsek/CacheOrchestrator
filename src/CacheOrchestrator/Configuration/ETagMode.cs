namespace CacheOrchestrator.Configuration;

/// <summary>
/// How the Output Cache policy sets the HTTP <c>ETag</c> response header.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Version"/> matches a bulk / snapshot domain (e.g. map tiles): one generation stamp
/// for every URL in the domain. <see cref="Resource"/> gives a distinct validator per URL or
/// resource id within that generation. <see cref="None"/> omits ETag (useful for short-TTL dynamic APIs).
/// </para>
/// <para>
/// ETag is a client/CDN revalidation signal. Server Output Cache keys are independent (per URL + vary).
/// </para>
/// </remarks>
public enum ETagMode
{
    /// <summary>
    /// Weak ETag derived only from domain <c>Version</c> (same value for every URL in the domain).
    /// Best for immutable snapshots until the next Version cutover.
    /// </summary>
    Version = 0,

    /// <summary>
    /// Do not set an <c>ETag</c> header.
    /// </summary>
    None = 1,

    /// <summary>
    /// Weak ETag from domain <c>Version</c> plus a per-resource key (resource id when known, otherwise path+query).
    /// Distinct per URL/entity; still a generation stamp, not a body content hash.
    /// </summary>
    Resource = 2
}
