using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Metadata;

namespace CacheOrchestrator.Identity;

/// <summary>
/// Binds the listed HTTP methods to bounded request-body content-hash identity (XxHash3).
/// Oversized bodies bypass caching (no silent truncation).
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class ContentHashCacheIdentityAttribute : Attribute, IEndpointMetadataProvider
{
    /// <summary>Default maximum body size (64 KiB).</summary>
    public const int DefaultMaxBodyBytes = 65_536;

    /// <summary>
    /// Creates a content-hash identity binding for the given methods.
    /// </summary>
    /// <param name="methods">HTTP methods that use content-hash identity (required, non-empty).</param>
    public ContentHashCacheIdentityAttribute(string[] methods)
    {
        ArgumentNullException.ThrowIfNull(methods);
        if (methods.Length == 0)
            throw new ArgumentException("At least one HTTP method is required.", nameof(methods));

        Methods = methods;
    }

    /// <summary>HTTP methods covered by this binding.</summary>
    public string[] Methods { get; }

    /// <summary>
    /// Maximum request body bytes to hash. Larger bodies bypass caching.
    /// Default: <see cref="DefaultMaxBodyBytes"/>.
    /// </summary>
    public int MaxBodyBytes { get; set; } = DefaultMaxBodyBytes;

    /// <inheritdoc />
    public static void PopulateMetadata(MethodInfo method, EndpointBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(builder);

        foreach (ContentHashCacheIdentityAttribute attr in GetAttributes(method))
        {
            string[] methods = CacheIdentityEndpointExtensions.NormalizeMethods(attr.Methods);
            CacheIdentityEndpointExtensions.ApplyBinding(
                builder,
                methods,
                CacheIdentityBinding.CreateContentHash(attr.MaxBodyBytes));
        }
    }

    private static IEnumerable<ContentHashCacheIdentityAttribute> GetAttributes(MethodInfo method)
    {
        foreach (ContentHashCacheIdentityAttribute attr in method.GetCustomAttributes<ContentHashCacheIdentityAttribute>(inherit: true))
            yield return attr;

        Type? declaring = method.DeclaringType;
        if (declaring is null)
            yield break;

        foreach (ContentHashCacheIdentityAttribute attr in declaring.GetCustomAttributes<ContentHashCacheIdentityAttribute>(inherit: true))
            yield return attr;
    }
}
