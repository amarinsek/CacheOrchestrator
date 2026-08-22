using CacheOrchestrator.Cluster;

namespace CacheOrchestrator.Invalidation;

/// <summary>
/// Outcome of a programmatic invalidation attempt (best-effort; does not throw on partial failure).
/// </summary>
public sealed class CacheInvalidationResult
{
    /// <summary>
    /// Creates a successful or partial result.
    /// </summary>
    public CacheInvalidationResult(
        string scope,
        IReadOnlyList<string> tags,
        bool fusionSucceeded,
        bool outputSucceeded,
        IReadOnlyList<string>? errors = null,
        ClusterPublishResult? clusterPublish = null,
        bool isSkipped = false)
    {
        Scope = scope ?? string.Empty;
        Tags = tags ?? [];
        FusionSucceeded = fusionSucceeded;
        OutputSucceeded = outputSucceeded;
        Errors = errors ?? [];
        ClusterPublish = clusterPublish;
        IsSkipped = isSkipped;
    }

    /// <summary>Human-readable scope label (domain, domain/id, or joined tags).</summary>
    public string Scope { get; }

    /// <summary>Tags that were targeted for eviction.</summary>
    public IReadOnlyList<string> Tags { get; }

    /// <summary><see langword="true"/> when all FusionCache removals for this scope succeeded.</summary>
    public bool FusionSucceeded { get; }

    /// <summary><see langword="true"/> when all Output Cache evictions for this scope succeeded.</summary>
    public bool OutputSucceeded { get; }

    /// <summary>
    /// <see langword="true"/> when this call was a no-op (empty domain/tags).
    /// <see cref="Succeeded"/> is <see langword="false"/> for skipped results.
    /// </summary>
    public bool IsSkipped { get; }

    /// <summary>
    /// <see langword="true"/> when both Fusion and Output Cache fully succeeded and the call was not skipped.
    /// Cluster publish failures do not flip this flag.
    /// </summary>
    public bool Succeeded => !IsSkipped && FusionSucceeded && OutputSucceeded;

    /// <summary>Non-fatal error messages collected during best-effort invalidation.</summary>
    public IReadOnlyList<string> Errors { get; }

    /// <summary>
    /// Cluster bus publish summary when peers were contacted; <see langword="null"/> when publish was skipped.
    /// </summary>
    public ClusterPublishResult? ClusterPublish { get; }

    /// <summary>
    /// Result used when the call was a no-op (empty domain/tags, nothing to do).
    /// </summary>
    public static CacheInvalidationResult Skipped(string reason) =>
        new(
            scope: "(skipped)",
            tags: [],
            fusionSucceeded: true,
            outputSucceeded: true,
            errors: string.IsNullOrWhiteSpace(reason) ? [] : [reason],
            isSkipped: true);

    /// <summary>
    /// Aggregates multiple domain results (for <see cref="ICacheOrchestratorInvalidator.InvalidateDomainsAsync"/>).
    /// </summary>
    public static CacheInvalidationResult Aggregate(IReadOnlyList<CacheInvalidationResult> parts)
    {
        ArgumentNullException.ThrowIfNull(parts);
        if (parts.Count == 0)
            return Skipped("No domains provided.");

        List<string> tags = [];
        List<string> errors = [];
        bool fusionOk = true;
        bool outputOk = true;
        bool anyWork = false;
        List<string> scopes = [];

        foreach (CacheInvalidationResult part in parts)
        {
            scopes.Add(part.Scope);
            tags.AddRange(part.Tags);
            errors.AddRange(part.Errors);
            fusionOk &= part.FusionSucceeded;
            outputOk &= part.OutputSucceeded;
            if (!part.IsSkipped)
                anyWork = true;
        }

        return new CacheInvalidationResult(
            scope: string.Join(',', scopes),
            tags: tags,
            fusionSucceeded: fusionOk,
            outputSucceeded: outputOk,
            errors: errors,
            isSkipped: !anyWork);
    }
}
