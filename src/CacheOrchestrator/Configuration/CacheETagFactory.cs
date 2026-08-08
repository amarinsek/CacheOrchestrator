using System.IO.Hashing;
using System.Text;
using Microsoft.Extensions.Primitives;

namespace CacheOrchestrator.Configuration;

/// <summary>
/// Builds weak ETag values for domain cache responses.
/// </summary>
public static class CacheETagFactory
{
    /// <summary>
    /// Precomputed domain-generation ETag from a Version string (same for every URL in the domain).
    /// </summary>
    public static StringValues FromVersion(string version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        ulong hash = XxHash3.HashToUInt64(Encoding.UTF8.GetBytes(version));
        return new StringValues($"W/\"{hash:x16}\"");
    }

    /// <summary>
    /// Per-resource ETag: domain Version hex plus a hash of the resource key (id or path).
    /// </summary>
    /// <param name="versionHex">Hex stamp already stored on <see cref="DomainCacheOptions.VersionHex"/>.</param>
    /// <param name="resourceKey">Resource id or path/query identity (non-empty).</param>
    public static StringValues FromVersionAndResource(string versionHex, string resourceKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(versionHex);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceKey);

        // versionHex\0resourceKey — keeps generation + resource in one weak validator
        int len = Encoding.UTF8.GetByteCount(versionHex) + 1 + Encoding.UTF8.GetByteCount(resourceKey);
        Span<byte> buffer = len <= 512 ? stackalloc byte[len] : new byte[len];
        int written = Encoding.UTF8.GetBytes(versionHex, buffer);
        buffer[written++] = 0;
        written += Encoding.UTF8.GetBytes(resourceKey, buffer[written..]);
        ulong hash = XxHash3.HashToUInt64(buffer[..written]);
        return new StringValues($"W/\"{versionHex}-{hash:x16}\"");
    }
}
