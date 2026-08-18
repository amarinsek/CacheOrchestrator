namespace CacheOrchestrator.AdminConsole.Models;

/// <summary>
/// Lightweight request checks for Console write APIs.
/// Throws <see cref="ArgumentException"/> (mapped to HTTP 400 by Minimal APIs).
/// </summary>
public static class AdminConsoleWriteValidators
{
    private static readonly HashSet<string> AllowedInvalidateScopes = new(StringComparer.OrdinalIgnoreCase)
    {
        "domain", "entity", "entityKind", "tags",
    };

    /// <summary>Validates invalidate body before fan-out.</summary>
    public static void Validate(AdminConsoleInvalidateRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateTarget(request.Target);

        string scope = (request.Scope ?? "").Trim();
        if (scope.Length == 0 || !AllowedInvalidateScopes.Contains(scope))
        {
            throw new ArgumentException(
                "Scope must be one of: domain, entity, entityKind, tags.",
                nameof(request));
        }

        if (scope.Equals("domain", StringComparison.OrdinalIgnoreCase)
            || scope.Equals("entity", StringComparison.OrdinalIgnoreCase)
            || scope.Equals("entityKind", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(request.Domain))
                throw new ArgumentException("Domain is required for this scope.", nameof(request));
        }

        if (scope.Equals("entity", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(request.EntityKind))
                throw new ArgumentException("EntityKind is required for entity scope.", nameof(request));
            if (string.IsNullOrWhiteSpace(request.EntityId))
                throw new ArgumentException("EntityId is required for entity scope.", nameof(request));
        }

        if (scope.Equals("entityKind", StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrWhiteSpace(request.EntityKind))
        {
            throw new ArgumentException("EntityKind is required for entityKind scope.", nameof(request));
        }

        if (scope.Equals("tags", StringComparison.OrdinalIgnoreCase)
            && (request.Tags is null || request.Tags.Length == 0
                || request.Tags.All(string.IsNullOrWhiteSpace)))
        {
            throw new ArgumentException("Tags must contain at least one non-empty tag.", nameof(request));
        }
    }

    /// <summary>Validates version body (target shape).</summary>
    public static void Validate(AdminConsoleVersionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateTarget(request.Target);
    }

    /// <summary>Validates TTL patch body (target + at least one field).</summary>
    public static void Validate(AdminConsoleTtlPatchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateTarget(request.Target);

        bool any =
            request.OutputCacheTtlSeconds is not null
            || request.FusionCacheSoftTtlSeconds is not null
            || request.FusionCacheHardTtlSeconds is not null
            || request.FusionCacheFailSafeSeconds is not null
            || request.ClientTtlSeconds is not null
            || request.ClientTtlMinSeconds is not null;

        if (!any)
            throw new ArgumentException("At least one TTL field must be set.", nameof(request));

        ValidateNonNegative(request.OutputCacheTtlSeconds, nameof(request.OutputCacheTtlSeconds));
        ValidateNonNegative(request.FusionCacheSoftTtlSeconds, nameof(request.FusionCacheSoftTtlSeconds));
        ValidateNonNegative(request.FusionCacheHardTtlSeconds, nameof(request.FusionCacheHardTtlSeconds));
        ValidateNonNegative(request.FusionCacheFailSafeSeconds, nameof(request.FusionCacheFailSafeSeconds));
        ValidateNonNegative(request.ClientTtlSeconds, nameof(request.ClientTtlSeconds));
        ValidateNonNegative(request.ClientTtlMinSeconds, nameof(request.ClientTtlMinSeconds));
    }

    private static void ValidateTarget(string? target)
    {
        if (string.IsNullOrWhiteSpace(target)
            || string.Equals(target, "all", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        const string prefix = "instance:";
        if (!target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            || target.Length <= prefix.Length
            || string.IsNullOrWhiteSpace(target[prefix.Length..]))
        {
            throw new ArgumentException("Target must be 'all' or 'instance:{id}'.", nameof(target));
        }
    }

    private static void ValidateNonNegative(int? seconds, string paramName)
    {
        if (seconds is < 0)
            throw new ArgumentException($"{paramName} must be >= 0.", paramName);
    }
}
