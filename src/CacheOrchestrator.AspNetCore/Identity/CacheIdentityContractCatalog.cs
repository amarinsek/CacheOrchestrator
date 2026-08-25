using System.Collections.ObjectModel;

namespace CacheOrchestrator.Identity;

/// <summary>
/// Startup catalog of registered <see cref="ICacheIdentityContract"/> instances keyed by name.
/// </summary>
internal sealed class CacheIdentityContractCatalog
{
    private readonly IReadOnlyDictionary<string, ICacheIdentityContract> _byName;

    public CacheIdentityContractCatalog(IEnumerable<ICacheIdentityContract> contracts)
    {
        ArgumentNullException.ThrowIfNull(contracts);

        Dictionary<string, ICacheIdentityContract> map = new(StringComparer.OrdinalIgnoreCase);
        foreach (ICacheIdentityContract contract in contracts)
        {
            ArgumentNullException.ThrowIfNull(contract);
            if (string.IsNullOrWhiteSpace(contract.Name))
            {
                throw new InvalidOperationException(
                    $"ICacheIdentityContract implementation '{contract.GetType().FullName}' has an empty Name.");
            }

            string name = contract.Name.Trim();
            if (string.Equals(name, CacheIdentities.Url, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"ICacheIdentityContract '{contract.GetType().FullName}' cannot use the reserved name '{CacheIdentities.Url}'.");
            }

            if (!map.TryAdd(name, contract))
            {
                throw new InvalidOperationException(
                    $"Duplicate cache identity contract name '{name}'. " +
                    $"Both '{map[name].GetType().FullName}' and '{contract.GetType().FullName}' registered.");
            }
        }

        _byName = new ReadOnlyDictionary<string, ICacheIdentityContract>(map);
    }

    public IReadOnlyDictionary<string, ICacheIdentityContract> ByName => _byName;

    public bool TryGet(string name, out ICacheIdentityContract contract) =>
        _byName.TryGetValue(name, out contract!);
}
