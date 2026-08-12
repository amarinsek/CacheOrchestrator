using CacheOrchestrator.Cluster;
using CacheOrchestrator.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.ServiceDiscovery;
using System.Net;

namespace CacheOrchestrator.Bus;

/// <summary>
/// Membership via <see cref="ServiceEndpointResolver"/> (configuration, DNS, platform providers).
/// </summary>
public sealed class ServiceDiscoveryClusterMembership : IClusterMembership
{
    private readonly ServiceEndpointResolver _resolver;
    private readonly IOptionsMonitor<CacheOrchestratorOptions> _options;
    private readonly ILogger<ServiceDiscoveryClusterMembership> _logger;
    private readonly TimeProvider _time;
#pragma warning disable IDE0330 // System.Threading.Lock is net9+; multi-target net8/net10 uses object.
    private readonly object _cacheLock = new();
#pragma warning restore IDE0330
    private IReadOnlyList<ClusterPeer>? _cached;
    private DateTimeOffset _cachedUntil = DateTimeOffset.MinValue;

    /// <summary>
    /// Initializes a new instance of the <see cref="ServiceDiscoveryClusterMembership"/> class.
    /// </summary>
    public ServiceDiscoveryClusterMembership(
        ServiceEndpointResolver resolver,
        IOptionsMonitor<CacheOrchestratorOptions> options,
        ILogger<ServiceDiscoveryClusterMembership> logger,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _resolver = resolver;
        _options = options;
        _logger = logger;
        _time = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public string Kind => "ServiceDiscovery";

    /// <inheritdoc />
    public async Task<IReadOnlyList<ClusterPeer>> GetPeersAsync(CancellationToken cancellationToken = default)
    {
        CacheOrchestratorOptions.ServiceDiscoveryMembershipOptions sd =
            _options.CurrentValue.Cluster.Bus.ServiceDiscovery;

        string? configuredName = sd.ServiceName;
        if (string.IsNullOrWhiteSpace(configuredName))
        {
            _logger.LogWarning(
                "Cluster:Bus:Membership=ServiceDiscovery but ServiceName is empty; returning no peers.");
            return [];
        }

        string scheme = string.IsNullOrWhiteSpace(sd.DefaultScheme)
            ? "http"
            : sd.DefaultScheme.Trim().TrimEnd(':');

        // Configuration provider resolves when the query includes a URI scheme
        // (e.g. "http://app1"). Bare names fall through to pass-through DNS only.
        string queryName = NormalizeServiceQuery(configuredName.Trim(), scheme);

        int cacheSeconds = Math.Clamp(sd.CacheSeconds, 0, 300);
        DateTimeOffset now = _time.GetUtcNow();

        if (cacheSeconds > 0)
        {
            lock (_cacheLock)
            {
                if (_cached is not null && now < _cachedUntil)
                    return _cached;
            }
        }

        ServiceEndpointSource source;
        try
        {
            source = await _resolver.GetEndpointsAsync(queryName, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Service discovery failed for service '{ServiceName}'", queryName);
            return [];
        }

        string peerIdPrefix = configuredName.Trim();
        if (peerIdPrefix.Contains("://", StringComparison.Ordinal))
        {
            // Strip scheme prefix for id labels: "http://app1" → "app1"
            int idx = peerIdPrefix.IndexOf("://", StringComparison.Ordinal);
            peerIdPrefix = peerIdPrefix[(idx + 3)..];
        }

        List<ClusterPeer> peers = [];
        int index = 0;
        foreach (ServiceEndpoint endpoint in source.Endpoints)
        {
            if (!TryCreateBaseUrl(endpoint.EndPoint, scheme, out Uri? baseUrl) || baseUrl is null)
                continue;

            // Skip bare pass-through names without a usable host:port (port 0).
            if (endpoint.EndPoint is DnsEndPoint dns && dns.Port <= 0
                && !IPAddress.TryParse(dns.Host, out _))
            {
                continue;
            }

            string id = $"{peerIdPrefix}-{index}";
            peers.Add(new ClusterPeer(id, baseUrl));
            index++;
        }

        if (cacheSeconds > 0)
        {
            lock (_cacheLock)
            {
                _cached = peers;
                _cachedUntil = now.AddSeconds(cacheSeconds);
            }
        }

        return peers;
    }

    /// <summary>
    /// Ensures the service discovery query includes a scheme so the configuration endpoint provider matches.
    /// </summary>
    internal static string NormalizeServiceQuery(string serviceName, string defaultScheme)
    {
        if (serviceName.Contains("://", StringComparison.Ordinal)
            || serviceName.Contains('+', StringComparison.Ordinal))
        {
            return serviceName;
        }

        return $"{defaultScheme}://{serviceName}";
    }

    internal static bool TryCreateBaseUrl(EndPoint endPoint, string scheme, out Uri? baseUrl)
    {
        baseUrl = null;
        if (endPoint is null)
            return false;

        // UriEndPoint exists in newer runtimes as a feature endpoint type; reflect-friendly cast via ToString fallback.
        if (endPoint is DnsEndPoint dns)
        {
            string host = dns.Host;
            int port = dns.Port;
            string authority = port > 0 ? $"{host}:{port}" : host;
            return Uri.TryCreate($"{scheme}://{authority}", UriKind.Absolute, out baseUrl);
        }

        if (endPoint is IPEndPoint ip)
        {
            string host = ip.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6
                ? $"[{ip.Address}]"
                : ip.Address.ToString();
            return Uri.TryCreate($"{scheme}://{host}:{ip.Port}", UriKind.Absolute, out baseUrl);
        }

        // Fallback: some providers expose host:port via ToString().
        string text = endPoint.ToString() ?? string.Empty;
        if (text.Contains("://", StringComparison.Ordinal))
            return Uri.TryCreate(text, UriKind.Absolute, out baseUrl);

        if (!string.IsNullOrWhiteSpace(text))
            return Uri.TryCreate($"{scheme}://{text.Trim()}", UriKind.Absolute, out baseUrl);

        return false;
    }
}
