using CacheOrchestrator.Edge.DependencyInjection;
using CacheOrchestrator.Edge.Providers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace CacheOrchestrator.Edge.Cloudflare;

/// <summary>Cloudflare edge-cache registration extensions.</summary>
public static class CloudflareEdgeBuilderExtensions
{
    /// <summary>Registers the Cloudflare tag-native edge provider.</summary>
    public static ICacheOrchestratorEdgeBuilder AddCloudflare(this ICacheOrchestratorEdgeBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddOptions<CloudflareEdgeConfiguration>()
            .Bind(builder.Configuration.GetSection(builder.ConfigSection))
            .ValidateOnStart();
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<CloudflareEdgeConfiguration>>(
                new CloudflareEdgeOptionsValidator(builder.Configuration.GetSection(builder.ConfigSection))));
        builder.Services.AddHttpClient(CloudflareEdgeProvider.HttpClientName, client =>
            client.BaseAddress = new Uri("https://api.cloudflare.com/client/v4/"));
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IEdgeResponseProvider, CloudflareEdgeProvider>());
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IEdgeInvalidationProvider, CloudflareEdgeProvider>());
        return builder;
    }
}
