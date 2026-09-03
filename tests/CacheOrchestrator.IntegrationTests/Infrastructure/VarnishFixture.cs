using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;

namespace CacheOrchestrator.IntegrationTests.Infrastructure;

/// <summary>Real nginx origin and Varnish xkey containers used by edge integration tests.</summary>
public sealed class VarnishFixture : IAsyncLifetime
{
    public const string VarnishImage = "varnish:9.0.3-5";
    public const string OriginImage = "nginx:1.27-alpine";

    private readonly INetwork _network;
    private readonly IContainer _origin;
    private readonly IContainer _varnish;

    public VarnishFixture()
    {
        string assets = Path.Combine(AppContext.BaseDirectory, "Edge", "Varnish");
        _network = new NetworkBuilder().WithName($"cache-orchestrator-varnish-{Guid.NewGuid():N}").Build();
        _origin = new ContainerBuilder(OriginImage)
            .WithNetwork(_network)
            .WithNetworkAliases("edge-origin")
            .WithBindMount(Path.Combine(assets, "nginx.conf"), "/etc/nginx/conf.d/default.conf", AccessMode.ReadOnly)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(80))
            .WithCleanUp(true)
            .Build();
        _varnish = new ContainerBuilder(VarnishImage)
            .WithNetwork(_network)
            .WithPortBinding(80, true)
            .WithBindMount(Path.Combine(assets, "default.vcl"), "/etc/varnish/default.vcl", AccessMode.ReadOnly)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(request => request
                .ForPort(80)
                .ForPath("/health")))
            .WithCleanUp(true)
            .Build();
    }

    public Uri Address => new($"http://{_varnish.Hostname}:{_varnish.GetMappedPublicPort(80)}");

    public async ValueTask InitializeAsync()
    {
        try
        {
            await _network.CreateAsync().ConfigureAwait(false);
            await _origin.StartAsync().ConfigureAwait(false);
            await _varnish.StartAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await DisposeAsync().ConfigureAwait(false);
            throw new InvalidOperationException(
                "Failed to start the Varnish integration topology. Docker must be available and able to pull the " +
                $"'{OriginImage}' and '{VarnishImage}' images.",
                ex);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _varnish.DisposeAsync().ConfigureAwait(false);
        await _origin.DisposeAsync().ConfigureAwait(false);
        await _network.DisposeAsync().ConfigureAwait(false);
    }
}

[CollectionDefinition("Varnish")]
public sealed class VarnishCollection : ICollectionFixture<VarnishFixture>;
