using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Metadata;

namespace CacheOrchestrator.Identity;

/// <summary>
/// Binds the listed HTTP methods on this action/endpoint to a named <see cref="ICacheIdentityContract"/>.
/// Use <see cref="CacheIdentities.Url"/> for explicit Url identity (including non-GET methods).
/// </summary>
/// <remarks>
/// Duplicate methods across <see cref="CacheIdentityAttribute"/> /
/// <see cref="ContentHashCacheIdentityAttribute"/> on the same action fail at build time (analyzer)
/// and at endpoint registration.
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class CacheIdentityAttribute : Attribute, IEndpointMetadataProvider
{
    /// <summary>
    /// Creates an identity binding for the given methods and contract name.
    /// </summary>
    /// <param name="methods">HTTP methods that use this contract (required, non-empty).</param>
    /// <param name="contractName">DI contract name, or <see cref="CacheIdentities.Url"/>.</param>
    public CacheIdentityAttribute(string[] methods, string contractName)
    {
        ArgumentNullException.ThrowIfNull(methods);
        if (methods.Length == 0)
            throw new ArgumentException("At least one HTTP method is required.", nameof(methods));
        ArgumentException.ThrowIfNullOrWhiteSpace(contractName);

        Methods = methods;
        ContractName = contractName.Trim();
    }

    /// <summary>HTTP methods covered by this binding.</summary>
    public string[] Methods { get; }

    /// <summary>Named contract or <see cref="CacheIdentities.Url"/>.</summary>
    public string ContractName { get; }

    /// <inheritdoc />
    public static void PopulateMetadata(MethodInfo method, EndpointBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(builder);

        foreach (CacheIdentityAttribute attr in GetAttributes(method))
        {
            string[] methods = CacheIdentityEndpointExtensions.NormalizeMethods(attr.Methods);
            CacheIdentityEndpointExtensions.ApplyBinding(
                builder,
                methods,
                CacheIdentityBinding.CreateNamed(attr.ContractName));
        }
    }

    private static IEnumerable<CacheIdentityAttribute> GetAttributes(MethodInfo method)
    {
        foreach (CacheIdentityAttribute attr in method.GetCustomAttributes<CacheIdentityAttribute>(inherit: true))
            yield return attr;

        Type? declaring = method.DeclaringType;
        if (declaring is null)
            yield break;

        foreach (CacheIdentityAttribute attr in declaring.GetCustomAttributes<CacheIdentityAttribute>(inherit: true))
            yield return attr;
    }
}
