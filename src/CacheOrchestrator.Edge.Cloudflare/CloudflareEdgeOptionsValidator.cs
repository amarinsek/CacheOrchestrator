using CacheOrchestrator.Edge.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace CacheOrchestrator.Edge.Cloudflare;

internal sealed class CloudflareEdgeOptionsValidator : IValidateOptions<CloudflareEdgeConfiguration>
{
    private readonly IConfigurationSection _configuration;

    public CloudflareEdgeOptionsValidator(IConfigurationSection configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        _configuration = configuration;
    }

    public ValidateOptionsResult Validate(string? name, CloudflareEdgeConfiguration options)
    {
        CacheOrchestratorEdgeOptions edgeOptions = new();
        _configuration.Bind(edgeOptions);
        List<string> failures = [];
        foreach ((string instanceName, EdgeInstanceOptions instance) in edgeOptions.EdgeInstances)
        {
            if (!string.Equals(instance.Provider, CloudflareEdgeProvider.ProviderName, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!options.EdgeInstances.TryGetValue(instanceName, out CloudflareEdgeInstanceContainer? container)
                || container.Cloudflare is null)
            {
                failures.Add($"Cache:EdgeInstances:{instanceName}:Cloudflare is required.");
                continue;
            }
            if (string.IsNullOrWhiteSpace(container.Cloudflare.ZoneId))
                failures.Add($"Cache:EdgeInstances:{instanceName}:Cloudflare:ZoneId is required.");
            if (string.IsNullOrWhiteSpace(container.Cloudflare.ApiToken))
                failures.Add($"Cache:EdgeInstances:{instanceName}:Cloudflare:ApiToken is required.");
        }
        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
