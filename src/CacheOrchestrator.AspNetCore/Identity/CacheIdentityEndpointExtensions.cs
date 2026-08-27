using Microsoft.AspNetCore.Builder;

namespace CacheOrchestrator.Identity;

/// <summary>
/// Fluent helpers that attach per-method cache identity bindings to Minimal API endpoints.
/// </summary>
public static class CacheIdentityEndpointExtensions
{
    /// <summary>
    /// Binds the listed HTTP methods to a named <see cref="ICacheIdentityContract"/>
    /// (or <see cref="CacheIdentities.Url"/> for explicit Url identity).
    /// </summary>
    /// <param name="builder">The route handler builder.</param>
    /// <param name="methods">HTTP methods that use this identity (required, non-empty).</param>
    /// <param name="contractName">DI contract name or <see cref="CacheIdentities.Url"/>.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    /// <exception cref="InvalidOperationException">Thrown when a method is bound twice on this endpoint.</exception>
    public static RouteHandlerBuilder WithCacheIdentity(
        this RouteHandlerBuilder builder,
        IEnumerable<string> methods,
        string contractName)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(methods);
        ArgumentException.ThrowIfNullOrWhiteSpace(contractName);

        string[] methodList = NormalizeMethods(methods);
        var binding = CacheIdentityBinding.CreateNamed(contractName);

        builder.Add(endpointBuilder =>
            ApplyBinding(endpointBuilder, methodList, binding));

        return builder;
    }

    /// <summary>
    /// Binds the listed HTTP methods to bounded request-body content-hash identity (XxHash3).
    /// Oversized bodies bypass caching (no silent truncation).
    /// </summary>
    /// <param name="builder">The route handler builder.</param>
    /// <param name="methods">HTTP methods that use content-hash identity (required, non-empty).</param>
    /// <param name="maxBodyBytes">Maximum body bytes to hash. Default 64 KiB.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    /// <exception cref="InvalidOperationException">Thrown when a method is bound twice on this endpoint.</exception>
    public static RouteHandlerBuilder WithContentHashCacheIdentity(
        this RouteHandlerBuilder builder,
        IEnumerable<string> methods,
        int maxBodyBytes = ContentHashCacheIdentityAttribute.DefaultMaxBodyBytes)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(methods);

        string[] methodList = NormalizeMethods(methods);
        var binding = CacheIdentityBinding.CreateContentHash(maxBodyBytes);

        builder.Add(endpointBuilder =>
            ApplyBinding(endpointBuilder, methodList, binding));

        return builder;
    }

    internal static void ApplyBinding(
        EndpointBuilder endpointBuilder,
        IReadOnlyList<string> methods,
        CacheIdentityBinding binding)
    {
        ArgumentNullException.ThrowIfNull(endpointBuilder);
        ArgumentNullException.ThrowIfNull(methods);
        ArgumentNullException.ThrowIfNull(binding);

        CacheIdentityEndpointMetadata metadata = GetOrCreate(endpointBuilder);
        string? displayName = endpointBuilder.DisplayName;
        for (int i = 0; i < methods.Count; i++)
            metadata.AddBinding(methods[i], binding, displayName);
    }

    internal static CacheIdentityEndpointMetadata GetOrCreate(EndpointBuilder endpointBuilder)
    {
        for (int i = 0; i < endpointBuilder.Metadata.Count; i++)
        {
            if (endpointBuilder.Metadata[i] is CacheIdentityEndpointMetadata existing)
                return existing;
        }

        CacheIdentityEndpointMetadata created = new();
        endpointBuilder.Metadata.Add(created);
        return created;
    }

    internal static string[] NormalizeMethods(IEnumerable<string> methods)
    {
        List<string> list = [];
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        foreach (string? method in methods)
        {
            if (string.IsNullOrWhiteSpace(method))
                throw new ArgumentException("HTTP methods must not be null or whitespace.", nameof(methods));

            string normalized = method.Trim().ToUpperInvariant();
            if (!seen.Add(normalized))
            {
                throw new ArgumentException(
                    $"Duplicate HTTP method '{normalized}' in the same identity helper call.",
                    nameof(methods));
            }

            list.Add(normalized);
        }

        if (list.Count == 0)
            throw new ArgumentException("At least one HTTP method is required.", nameof(methods));

        return list.ToArray();
    }
}
