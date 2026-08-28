using CacheOrchestrator.Configuration;
using CacheOrchestrator.Orchestration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CacheOrchestrator.Diagnostics;

internal sealed class DataCacheProviderStartupDiagnostic : IHostedService
{
    private readonly IDataCacheProvider _provider;
    private readonly IOptions<CacheOrchestratorOptions> _options;
    private readonly ILogger<DataCacheProviderStartupDiagnostic> _logger;

    public DataCacheProviderStartupDiagnostic(
        IDataCacheProvider provider,
        IOptions<CacheOrchestratorOptions> options,
        ILogger<DataCacheProviderStartupDiagnostic> logger)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _provider = provider;
        _options = options;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (_provider is NullDataCacheProvider && IsDataCacheEnabled(_options.Value))
        {
            _logger.LogWarning(
                "Data Cache is enabled, but no Data Cache provider is registered. " +
                "Factories will run uncached. Register FusionCache or HybridCache, or explicitly disable Data Cache.");
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    internal static bool IsDataCacheEnabled(CacheOrchestratorOptions options)
    {
        bool defaultEnabled = options.DomainDefaults.DataCache?.Enabled ?? true;
        if (defaultEnabled)
            return true;

        return options.Domains.Values.Any(domain => domain.DataCache?.Enabled == true);
    }
}
