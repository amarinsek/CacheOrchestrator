using CacheOrchestrator.Edge.DependencyInjection;
using CacheOrchestrator.Edge.Providers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace CacheOrchestrator.Edge.Varnish;

/// <summary>Varnish edge-cache registration extensions.</summary>
public static class VarnishEdgeBuilderExtensions
{
    /// <summary>Registers the Varnish xkey response and invalidation providers.</summary>
    public static ICacheOrchestratorEdgeBuilder AddVarnish(this ICacheOrchestratorEdgeBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddOptions<VarnishEdgeConfiguration>()
            .Bind(builder.Configuration.GetSection(builder.ConfigSection))
            .ValidateOnStart();
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<VarnishEdgeConfiguration>>(
                new VarnishEdgeOptionsValidator(builder.Configuration.GetSection(builder.ConfigSection))));
        builder.Services.AddHttpClient(VarnishEdgeProvider.HttpClientName);
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IEdgeResponseProvider, VarnishEdgeProvider>());
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IEdgeInvalidationProvider, VarnishEdgeProvider>());
        return builder;
    }
}
