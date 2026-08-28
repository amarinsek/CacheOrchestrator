using Microsoft.Extensions.Primitives;
using System.IO.Hashing;
using System.Text;

namespace CacheOrchestrator.Configuration;

/// <summary>Builds weak ETag values for ASP.NET Core cache responses.</summary>
public static class CacheETagFactory
{
    /// <summary>Builds a domain-generation ETag from a Version string.</summary>
    public static StringValues FromVersion(string version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        ulong hash = XxHash3.HashToUInt64(Encoding.UTF8.GetBytes(version));
        return new StringValues($"W/\"{hash:x16}\"");
    }

    /// <summary>Builds a per-resource ETag from Version material and a resource key.</summary>
    public static StringValues FromVersionAndResource(string versionHex, string resourceKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(versionHex);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceKey);
        return FromVersionAndResource(versionHex, resourceKey.AsSpan(), default, default);
    }

    /// <summary>Builds a per-resource ETag directly from key segments.</summary>
    public static StringValues FromVersionAndResource(
        string versionHex,
        ReadOnlySpan<char> part1,
        ReadOnlySpan<char> part2 = default,
        ReadOnlySpan<char> part3 = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(versionHex);
        if (part1.IsEmpty && part2.IsEmpty && part3.IsEmpty)
            throw new ArgumentException("At least one resource segment must be non-empty.", nameof(part1));

        int length = Encoding.UTF8.GetByteCount(versionHex) + 1
            + Encoding.UTF8.GetByteCount(part1)
            + Encoding.UTF8.GetByteCount(part2)
            + Encoding.UTF8.GetByteCount(part3);
        Span<byte> buffer = length <= 512 ? stackalloc byte[length] : new byte[length];

        int written = Encoding.UTF8.GetBytes(versionHex, buffer);
        buffer[written++] = 0;
        if (!part1.IsEmpty)
            written += Encoding.UTF8.GetBytes(part1, buffer[written..]);
        if (!part2.IsEmpty)
            written += Encoding.UTF8.GetBytes(part2, buffer[written..]);
        if (!part3.IsEmpty)
            written += Encoding.UTF8.GetBytes(part3, buffer[written..]);

        ulong hash = XxHash3.HashToUInt64(buffer[..written]);
        return new StringValues($"W/\"{versionHex}-{hash:x16}\"");
    }
}
