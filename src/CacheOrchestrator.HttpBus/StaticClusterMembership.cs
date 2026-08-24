using CacheOrchestrator.Cluster;
using CacheOrchestrator.Configuration;
using Microsoft.Extensions.Options;

namespace CacheOrchestrator.HttpBus;

/// <summary>
/// Membership from <c>Cache:Cluster:Bus:Static:Instances</c>.
/// </summary>
public sealed class StaticClusterMembership : IClusterMembership
{
    private readonly IOptionsMonitor<CacheOrchestratorOptions> _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="StaticClusterMembership"/> class.
    /// </summary>
    public StaticClusterMembership(IOptionsMonitor<CacheOrchestratorOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    /// <inheritdoc />
    public string Kind => "Static";

    /// <inheritdoc />
    public Task<IReadOnlyList<ClusterPeer>> GetPeersAsync(CancellationToken cancellationToken = default)
    {
        CacheOrchestratorOptions.StaticClusterMembershipOptions staticOpts =
            _options.CurrentValue.Cluster.Bus.Static;

        List<ClusterPeer> peers = [];
        foreach (CacheOrchestratorOptions.StaticClusterPeerOptions? entry in staticOpts.Instances)
        {
            if (entry is null)
                continue;
            if (string.IsNullOrWhiteSpace(entry.Id) || string.IsNullOrWhiteSpace(entry.Url))
                continue;

            if (!Uri.TryCreate(entry.Url.Trim(), UriKind.Absolute, out Uri? uri))
                continue;

            peers.Add(new ClusterPeer(entry.Id.Trim(), uri));
        }

        return Task.FromResult<IReadOnlyList<ClusterPeer>>(peers);
    }
}
