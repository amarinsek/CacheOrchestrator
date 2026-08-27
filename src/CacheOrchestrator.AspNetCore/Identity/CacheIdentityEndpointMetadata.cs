using System.Diagnostics.CodeAnalysis;

namespace CacheOrchestrator.Identity;

/// <summary>
/// Per-endpoint map of HTTP method → identity binding.
/// Absent from endpoint metadata means implicit GET/HEAD → Url (hot path does not allocate this type).
/// </summary>
public sealed class CacheIdentityEndpointMetadata
{
    private readonly Dictionary<string, CacheIdentityBinding> _bindings =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Number of method bindings on this endpoint.</summary>
    public int Count => _bindings.Count;

    /// <summary>True after startup has resolved named contract instances.</summary>
    public bool IsResolved { get; private set; }

    /// <summary>
    /// Looks up the binding for <paramref name="httpMethod"/> (case-insensitive).
    /// </summary>
    public bool TryGetBinding(string httpMethod, [NotNullWhen(true)] out CacheIdentityBinding? binding)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(httpMethod);
        return _bindings.TryGetValue(httpMethod, out binding);
    }

    /// <summary>Enumerates configured method → binding pairs.</summary>
    public IReadOnlyDictionary<string, CacheIdentityBinding> Bindings => _bindings;

    internal void AddBinding(string httpMethod, CacheIdentityBinding binding, string? endpointDisplayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(httpMethod);
        ArgumentNullException.ThrowIfNull(binding);

        string method = httpMethod.Trim().ToUpperInvariant();
        if (_bindings.ContainsKey(method))
        {
            string endpoint = string.IsNullOrWhiteSpace(endpointDisplayName)
                ? "(unnamed endpoint)"
                : endpointDisplayName;
            throw new InvalidOperationException(
                $"Duplicate cache identity binding for HTTP method '{method}' on endpoint '{endpoint}'. " +
                "Each method may have at most one identity binding.");
        }

        _bindings[method] = binding;
    }

    internal void MarkResolved() => IsResolved = true;
}
