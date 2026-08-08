using CacheOrchestrator.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace CacheOrchestrator.Diagnostics;

/// <summary>
/// Aggregates registered <see cref="ICacheOrchestratorHealthProbe"/> instances into one health check.
/// </summary>
internal sealed class CacheOrchestratorHealthCheck : IHealthCheck
{
    private readonly IOptionsMonitor<CacheOrchestratorOptions> _options;
    private readonly IEnumerable<ICacheOrchestratorHealthProbe> _probes;

    /// <summary>
    /// Initializes a new instance of the <see cref="CacheOrchestratorHealthCheck"/> class.
    /// </summary>
    public CacheOrchestratorHealthCheck(
        IOptionsMonitor<CacheOrchestratorOptions> options,
        IEnumerable<ICacheOrchestratorHealthProbe> probes)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(probes);

        _options = options;
        _probes = probes;
    }

    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        CacheOrchestratorOptions opts = _options.CurrentValue;
        Dictionary<string, object> data = new()
        {
            ["output_provider"] = opts.OutputCache.Provider ?? "InMemory"
        };

        foreach ((string? instanceName, CacheOrchestratorOptions.FusionCacheInstanceOptions? instanceOpts) in opts.FusionCacheInstances)
            data[$"fusion_instance:{instanceName}"] = instanceOpts.Provider ?? "InMemory";

        List<ICacheOrchestratorHealthProbe> probes = [.. _probes];
        if (probes.Count == 0)
            return HealthCheckResult.Healthy("No cache health probes registered.", data);

        List<string> failures = [];

        foreach (ICacheOrchestratorHealthProbe probe in probes)
        {
            try
            {
                using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(context.Registration.Timeout);
                await probe.ProbeAsync(cts.Token).ConfigureAwait(false);
                data[$"probe:{probe.Name}"] = "ok";
            }
            catch (Exception ex)
            {
                data[$"probe:{probe.Name}"] = "fail";
                data[$"probe:{probe.Name}:error"] = ex.Message;
                failures.Add($"{probe.Name}: {ex.Message}");
            }
        }

        if (failures.Count == 0)
            return HealthCheckResult.Healthy("All cache probes succeeded.", data);

        string description = "One or more cache probes failed: " + string.Join("; ", failures);
        return new HealthCheckResult(
            context.Registration.FailureStatus,
            description,
            data: data);
    }
}