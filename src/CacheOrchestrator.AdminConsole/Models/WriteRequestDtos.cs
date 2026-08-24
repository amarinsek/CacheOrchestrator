namespace CacheOrchestrator.AdminConsole.Models;

/// <summary>Invalidate request for the Admin Console App (cluster-wide).</summary>
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
}

/// <summary>Version request (cluster-wide).</summary>
public sealed class AdminConsoleVersionRequest
{
    /// <summary>New version token; empty generates a stamp on each instance.</summary>
    public string? Version { get; set; }
}

/// <summary>
/// TTL patch (cluster-wide).
/// Prefer <see cref="AdminConsoleSettingsPatchRequest"/> / <c>PATCH /api/domains/{domain}/settings</c>.
/// </summary>
[Obsolete("Use AdminConsoleSettingsPatchRequest and PATCH /api/domains/{domain}/settings. This DTO remains for compatibility.")]
public sealed class AdminConsoleTtlPatchRequest
{
    /// <summary>Output Cache TTL seconds.</summary>
    public int? OutputCacheTtlSeconds { get; set; }

    /// <summary>Data-cache TTL seconds.</summary>
    public int? DataCacheTtlSeconds { get; set; }

    /// <summary>Client TTL seconds.</summary>
    public int? ClientTtlSeconds { get; set; }

    /// <summary>Client min TTL seconds.</summary>
    public int? ClientTtlMinSeconds { get; set; }
}

/// <summary>Sparse domain settings patch (cluster-wide).</summary>
public sealed class AdminConsoleSettingsPatchRequest
{
    /// <summary>CamelCase setting id → JSON value (overlay catalog entries only).</summary>
    public Dictionary<string, System.Text.Json.JsonElement>? Settings { get; set; }
}
