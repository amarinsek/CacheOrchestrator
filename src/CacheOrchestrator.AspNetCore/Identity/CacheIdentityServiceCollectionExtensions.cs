using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CacheOrchestrator.Identity;

/// <summary>
/// DI registration for named <see cref="ICacheIdentityContract"/> implementations.
/// </summary>
public static class CacheIdentityServiceCollectionExtensions
{
    /// <summary>
    /// Registers a singleton <see cref="ICacheIdentityContract"/> used by endpoint identity bindings.
    /// Contract instances are resolved onto endpoint metadata at host start — not per request.
    /// </summary>
    /// <typeparam name="TContract">Contract implementation type.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddCacheIdentityContract<TContract>(this IServiceCollection services)
        where TContract : class, ICacheIdentityContract
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<TContract>();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<ICacheIdentityContract, TContract>(
                sp => sp.GetRequiredService<TContract>()));
        return services;
    }
}
