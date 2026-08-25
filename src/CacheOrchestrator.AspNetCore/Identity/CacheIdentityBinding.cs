namespace CacheOrchestrator.Identity;

/// <summary>
/// Resolved per-method identity strategy stored on endpoint metadata.
/// Contract instances are set at startup — not looked up by name per request.
/// </summary>
public sealed class CacheIdentityBinding
{
    private CacheIdentityBinding(
        CacheIdentityKind kind,
        string? contractName,
        ICacheIdentityContract? contract,
        int maxBodyBytes)
    {
        Kind = kind;
        ContractName = contractName;
        Contract = contract;
        MaxBodyBytes = maxBodyBytes;
    }

    /// <summary>Identity strategy kind.</summary>
    public CacheIdentityKind Kind { get; }

    /// <summary>Contract name when <see cref="Kind"/> is <see cref="CacheIdentityKind.NamedContract"/>.</summary>
    public string? ContractName { get; }

    /// <summary>
    /// Resolved contract instance when <see cref="Kind"/> is <see cref="CacheIdentityKind.NamedContract"/>.
    /// Set during startup resolution; never resolved per request.
    /// </summary>
    public ICacheIdentityContract? Contract { get; private set; }

    /// <summary>
    /// Maximum request body bytes for content-hash identity.
    /// Bodies larger than this bypass caching (no silent truncation).
    /// </summary>
    public int MaxBodyBytes { get; }

    /// <summary>
    /// Whether the built-in path must buffer the request body before identity runs.
    /// Named contracts that read the body should call <c>EnableBuffering</c> themselves.
    /// </summary>
    public bool RequiresRequestBody => Kind == CacheIdentityKind.ContentHash;

    internal static CacheIdentityBinding CreateUrl() =>
        new(CacheIdentityKind.Url, CacheIdentities.Url, contract: null, maxBodyBytes: 0);

    internal static CacheIdentityBinding CreateNamed(string contractName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contractName);
        string name = contractName.Trim();
        if (string.Equals(name, CacheIdentities.Url, StringComparison.Ordinal))
            return CreateUrl();

        return new(CacheIdentityKind.NamedContract, name, contract: null, maxBodyBytes: 0);
    }

    internal static CacheIdentityBinding CreateContentHash(int maxBodyBytes)
    {
        if (maxBodyBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxBodyBytes), maxBodyBytes, "MaxBodyBytes must be positive.");

        return new(CacheIdentityKind.ContentHash, contractName: null, contract: null, maxBodyBytes);
    }

    internal void SetContract(ICacheIdentityContract contract)
    {
        ArgumentNullException.ThrowIfNull(contract);
        if (Kind != CacheIdentityKind.NamedContract)
            throw new InvalidOperationException("Only named-contract bindings can receive a contract instance.");

        Contract = contract;
    }
}
