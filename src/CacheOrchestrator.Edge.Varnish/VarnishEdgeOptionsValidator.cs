using CacheOrchestrator.Edge.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace CacheOrchestrator.Edge.Varnish;

internal sealed class VarnishEdgeOptionsValidator : IValidateOptions<VarnishEdgeConfiguration>
{
    private readonly IConfigurationSection _configuration;

    public VarnishEdgeOptionsValidator(IConfigurationSection configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        _configuration = configuration;
    }

    public ValidateOptionsResult Validate(string? name, VarnishEdgeConfiguration options)
    {
        CacheOrchestratorEdgeOptions edgeOptions = new();
        _configuration.Bind(edgeOptions);
        List<string> failures = [];
        foreach ((string instanceName, EdgeInstanceOptions instance) in edgeOptions.EdgeInstances)
        {
            if (!string.Equals(instance.Provider, VarnishEdgeProvider.ProviderName, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!options.EdgeInstances.TryGetValue(instanceName, out VarnishEdgeInstanceContainer? container)
                || container.Varnish is null)
            {
                failures.Add($"Cache:EdgeInstances:{instanceName}:Varnish is required.");
                continue;
            }

            VarnishEdgeInstanceOptions settings = container.Varnish;
            if (!Uri.TryCreate(settings.PurgeUrl, UriKind.Absolute, out Uri? purgeUri)
                || (purgeUri.Scheme != Uri.UriSchemeHttp && purgeUri.Scheme != Uri.UriSchemeHttps))
            {
                failures.Add($"Cache:EdgeInstances:{instanceName}:Varnish:PurgeUrl must be an absolute HTTP or HTTPS URL.");
            }
            if (settings.ApiKey is not null && string.IsNullOrWhiteSpace(settings.ApiKeyHeaderName))
            {
                failures.Add($"Cache:EdgeInstances:{instanceName}:Varnish:ApiKeyHeaderName is required when ApiKey is set.");
            }
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
