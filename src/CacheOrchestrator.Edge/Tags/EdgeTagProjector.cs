using System.Security.Cryptography;
using System.Text;

namespace CacheOrchestrator.Edge.Tags;

/// <summary>Projects canonical CacheOrchestrator tags into fixed-length opaque edge tags.</summary>
public sealed class EdgeTagProjector
{
    /// <summary>Projects one canonical tag using a stable versioned format.</summary>
    public string Project(string edgeNamespace, string canonicalTag)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(edgeNamespace);
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalTag);

        string namespaceHash = Hash(edgeNamespace, 8);
        string tagHash = Hash(canonicalTag, 32);
        return $"coe1-{namespaceHash}-{tagHash}";
    }

    private static string Hash(string value, int bytes)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash.AsSpan(0, bytes)).ToLowerInvariant();
    }
}
