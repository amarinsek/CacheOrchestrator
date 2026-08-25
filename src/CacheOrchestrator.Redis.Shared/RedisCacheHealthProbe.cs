using CacheOrchestrator.Diagnostics;
using StackExchange.Redis;

namespace CacheOrchestrator.Redis;

/// <summary>
/// Health probe that pings a Redis connection via <see cref="IConnectionMultiplexer"/>.
/// </summary>
internal sealed class RedisCacheHealthProbe : ICacheOrchestratorHealthProbe
{
    private readonly IConnectionMultiplexer _multiplexer;

    public RedisCacheHealthProbe(string probeName, IConnectionMultiplexer multiplexer)
    {
        ArgumentException.ThrowIfNullOrEmpty(probeName);
        ArgumentNullException.ThrowIfNull(multiplexer);

        Name = probeName;
        _multiplexer = multiplexer;
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public async Task ProbeAsync(CancellationToken cancellationToken = default)
    {
        if (!_multiplexer.IsConnected)
            throw new InvalidOperationException($"Redis multiplexer '{Name}' is not connected.");

        IDatabase db = _multiplexer.GetDatabase();
        await db.PingAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
    }
}
