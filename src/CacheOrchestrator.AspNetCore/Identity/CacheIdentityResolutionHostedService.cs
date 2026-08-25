using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CacheOrchestrator.Identity;

/// <summary>
/// Resolves named identity contracts onto endpoint metadata once the host has started
/// (endpoints from <c>Map*</c> are present). Unknown contract names fail fast.
/// </summary>
/// <remarks>
/// <see cref="IHostedService.StartAsync"/> can run before route endpoints are visible on
/// <see cref="EndpointDataSource"/>; resolution is therefore deferred to
/// <see cref="IHostApplicationLifetime.ApplicationStarted"/>. Request paths also resolve
/// lazily if metadata is still unresolved (see <see cref="CacheIdentityEndpointResolver"/>).
/// </remarks>
internal sealed class CacheIdentityResolutionHostedService : IHostedService
{
    private readonly IServiceProvider _services;
    private readonly IHostApplicationLifetime _lifetime;

    public CacheIdentityResolutionHostedService(
        IServiceProvider services,
        IHostApplicationLifetime lifetime)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(lifetime);
        _services = services;
        _lifetime = lifetime;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        EndpointDataSource dataSource = _services.GetRequiredService<EndpointDataSource>();
        if (dataSource.Endpoints.Count > 0)
        {
            CacheIdentityEndpointResolver.ResolveAll(_services);
            return Task.CompletedTask;
        }

        _lifetime.ApplicationStarted.Register(static state =>
        {
            CacheIdentityEndpointResolver.ResolveAll((IServiceProvider)state!);
        }, _services);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
