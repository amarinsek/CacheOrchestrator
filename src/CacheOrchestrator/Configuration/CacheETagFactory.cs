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

        return FromVersionAndResource(versionHex, resourceKey.AsSpan(), default, default);
    }

    /// <summary>
    /// Per-resource ETag hashed directly from segments, avoiding string concatenation allocations.
    /// </summary>
    public static StringValues FromVersionAndResource(
        string versionHex, 
        ReadOnlySpan<char> part1, 
        ReadOnlySpan<char> part2 = default, 
        ReadOnlySpan<char> part3 = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(versionHex);

        int len = Encoding.UTF8.GetByteCount(versionHex) + 1 
                  + Encoding.UTF8.GetByteCount(part1)
                  + Encoding.UTF8.GetByteCount(part2)
                  + Encoding.UTF8.GetByteCount(part3);

        Span<byte> buffer = len <= 512 ? stackalloc byte[len] : new byte[len];
        
        int written = Encoding.UTF8.GetBytes(versionHex, buffer);
        buffer[written++] = 0;
        
        if (!part1.IsEmpty) written += Encoding.UTF8.GetBytes(part1, buffer[written..]);
        if (!part2.IsEmpty) written += Encoding.UTF8.GetBytes(part2, buffer[written..]);
        if (!part3.IsEmpty) written += Encoding.UTF8.GetBytes(part3, buffer[written..]);

        ulong hash = XxHash3.HashToUInt64(buffer[..written]);
        return new StringValues($"W/\"{versionHex}-{hash:x16}\"");
    }
}
