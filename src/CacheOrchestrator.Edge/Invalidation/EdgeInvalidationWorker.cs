using CacheOrchestrator.Edge.Configuration;
using CacheOrchestrator.Edge.Diagnostics;
using CacheOrchestrator.Edge.Providers;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Threading.Channels;

namespace CacheOrchestrator.Edge.Invalidation;

internal sealed class EdgeInvalidationWorker : BackgroundService
{
    private readonly EdgeInvalidationChannel _channel;
    private readonly EdgeProviderCatalog _providers;
    private readonly IOptionsMonitor<CacheOrchestratorEdgeOptions> _options;
    private readonly ILogger<EdgeInvalidationWorker> _logger;
    private CancellationToken _shutdownToken;

    public EdgeInvalidationWorker(
        EdgeInvalidationChannel channel,
        EdgeProviderCatalog providers,
        IOptionsMonitor<CacheOrchestratorEdgeOptions> options,
        ILogger<EdgeInvalidationWorker> logger)
    {
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _channel = channel;
        _providers = providers;
        _options = options;
        _logger = logger;
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        _shutdownToken = cancellationToken;
        _channel.Channel.Writer.TryComplete();
        return base.StopAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        ChannelReader<EdgeInvalidationJob> reader = _channel.Channel.Reader;
        EdgeInvalidationJob? pending = null;
        try
        {
            while (await reader.WaitToReadAsync(stoppingToken).ConfigureAwait(false))
            {
                if (!reader.TryRead(out pending))
                    continue;

                int flushSeconds = _options.CurrentValue.EdgeQueue.FlushIntervalSeconds;
                if (flushSeconds > 0)
                    await Task.Delay(TimeSpan.FromSeconds(flushSeconds), stoppingToken).ConfigureAwait(false);

                var groups = new Dictionary<string, (string ProviderName, HashSet<string> Tags)>(
                    StringComparer.OrdinalIgnoreCase);
                Add(pending, groups);
                pending = null;
                while (reader.TryRead(out EdgeInvalidationJob? job))
                    Add(job, groups);

                await InvalidateGroupsAsync(groups, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            var groups = new Dictionary<string, (string ProviderName, HashSet<string> Tags)>(
                StringComparer.OrdinalIgnoreCase);
            if (pending is not null)
                Add(pending, groups);
            while (reader.TryRead(out EdgeInvalidationJob? job))
                Add(job, groups);

            await InvalidateGroupsAsync(groups, _shutdownToken).ConfigureAwait(false);
        }
    }

    private async Task InvalidateGroupsAsync(
        Dictionary<string, (string ProviderName, HashSet<string> Tags)> groups,
        CancellationToken cancellationToken)
    {
        foreach ((string instanceName, (string providerName, HashSet<string> tags)) in groups)
            await InvalidateGroupAsync(instanceName, providerName, tags, cancellationToken).ConfigureAwait(false);
    }

    private static void Add(
        EdgeInvalidationJob job,
        Dictionary<string, (string ProviderName, HashSet<string> Tags)> groups)
    {
        if (!groups.TryGetValue(
                job.InstanceName,
                out (string ProviderName, HashSet<string> Tags) group))
        {
            group = (job.ProviderName, new HashSet<string>(StringComparer.Ordinal));
            groups.Add(job.InstanceName, group);
        }

        foreach (string tag in job.Tags)
            group.Tags.Add(tag);
    }

    private async Task InvalidateGroupAsync(
        string instanceName,
        string providerName,
        HashSet<string> tags,
        CancellationToken cancellationToken)
    {
        IEdgeInvalidationProvider provider = _providers.ResolveInvalidation(providerName);
        int batchSize = provider.Capabilities.MaxInvalidationBatchSize;
        string[] allTags = [.. tags];
        for (int offset = 0; offset < allTags.Length; offset += batchSize)
        {
            int count = Math.Min(batchSize, allTags.Length - offset);
            string[] batch = new string[count];
            Array.Copy(allTags, offset, batch, 0, count);
            await InvalidateBatchAsync(provider, instanceName, batch, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task InvalidateBatchAsync(
        IEdgeInvalidationProvider provider,
        string instanceName,
        IReadOnlyList<string> tags,
        CancellationToken cancellationToken)
    {
        EdgeQueueOptions queueOptions = _options.CurrentValue.EdgeQueue;
        for (int attempt = 1; attempt <= queueOptions.MaxAttempts; attempt++)
        {
            EdgeInvalidationResult result;
            try
            {
                result = await provider.InvalidateAsync(
                    new EdgeInvalidationRequest { InstanceName = instanceName, Tags = tags },
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                result = new EdgeInvalidationResult
                {
                    IsTransient = true,
                    Error = ex.GetType().Name
                };
            }

            if (result.Succeeded)
            {
                EdgeMetrics.RecordPurged(instanceName, provider.Name, tags.Count);
                _logger.LogDebug(
                    "Edge invalidation succeeded for instance '{Instance}' provider={Provider} tags={TagCount}",
                    instanceName,
                    provider.Name,
                    tags.Count);
                return;
            }

            if (!result.IsTransient || attempt == queueOptions.MaxAttempts)
            {
                EdgeMetrics.RecordFailure(instanceName, provider.Name, result.IsTransient ? "exhausted" : "permanent");
                _logger.LogWarning(
                    "Edge invalidation failed for instance '{Instance}' provider={Provider} tags={TagCount} attempt={Attempt}: {Error}",
                    instanceName,
                    provider.Name,
                    tags.Count,
                    attempt,
                    result.Error ?? "provider rejected request");
                return;
            }

            TimeSpan delay = result.RetryAfter ?? TimeSpan.FromSeconds(
                queueOptions.RetryBaseDelaySeconds * Math.Pow(2, attempt - 1));
            delay += TimeSpan.FromMilliseconds(Random.Shared.Next(0, 251));
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }
    }
}
