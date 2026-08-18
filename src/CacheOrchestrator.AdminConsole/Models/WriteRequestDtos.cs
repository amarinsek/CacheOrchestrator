namespace CacheOrchestrator.AdminConsole.Models;

/// <summary>Invalidate request for the Admin Console App (adds multi-instance target).</summary>
public sealed class AdminConsoleInvalidateRequest
{
    /// <summary><c>domain</c>, <c>entity</c>, <c>entityKind</c>, or <c>tags</c>.</summary>
    public string Scope { get; set; } = "domain";

    /// <summary>Domain name.</summary>
    public string? Domain { get; set; }

    /// <summary>Entity kind (required for entity / entityKind scopes).</summary>
    public string? EntityKind { get; set; }

    /// <summary>Entity id.</summary>
    public string? EntityId { get; set; }

    /// <summary>Custom tags.</summary>
    public string[]? Tags { get; set; }

    /// <summary><c>all</c> or <c>instance:{id}</c>.</summary>
    public string Target { get; set; } = "all";
}

/// <summary>Version request with multi-instance target.</summary>
public sealed class AdminConsoleVersionRequest
{
    /// <summary>New version token; empty generates a stamp on each instance.</summary>
    public string? Version { get; set; }

    /// <summary><c>all</c> or <c>instance:{id}</c>.</summary>
    public string Target { get; set; } = "all";
}

/// <summary>TTL patch with multi-instance target.</summary>
public sealed class AdminConsoleTtlPatchRequest
{
    /// <summary>Output Cache TTL seconds.</summary>
    public int? OutputCacheTtlSeconds { get; set; }

    /// <summary>Fusion soft TTL seconds.</summary>
    public int? FusionCacheSoftTtlSeconds { get; set; }

    /// <summary>Fusion hard TTL seconds.</summary>
    public int? FusionCacheHardTtlSeconds { get; set; }

    /// <summary>Fusion fail-safe seconds.</summary>
    public int? FusionCacheFailSafeSeconds { get; set; }

    /// <summary>Client TTL seconds.</summary>
    public int? ClientTtlSeconds { get; set; }

    /// <summary>Client min TTL seconds.</summary>
    public int? ClientTtlMinSeconds { get; set; }

    /// <summary><c>all</c> or <c>instance:{id}</c>.</summary>
    public string Target { get; set; } = "all";
}
