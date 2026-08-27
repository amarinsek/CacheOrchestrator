using CacheOrchestrator.Identity;
using CacheOrchestrator.OutputCache;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace CacheOrchestrator.AspNetCore.UnitTests.Identity;

public class CacheIdentityEndpointExtensionsTests
{
    [Fact]
    public void WithCacheIdentity_AddsMetadataForListedMethods()
    {
        using WebApplication app = CreateApp();
        RouteHandlerBuilder builder = app.MapMethods(
            "/search",
            ["GET", "POST"],
            () => Results.Ok());

        builder.WithCacheIdentity(["GET", "HEAD", "POST"], "product-search-v1");

        CacheIdentityEndpointMetadata meta = GetIdentity(app, builder);
        meta.Count.Should().Be(3);
        meta.TryGetBinding("GET", out CacheIdentityBinding? get).Should().BeTrue();
        get!.Kind.Should().Be(CacheIdentityKind.NamedContract);
        get.ContractName.Should().Be("product-search-v1");
        meta.TryGetBinding("post", out _).Should().BeTrue();
        meta.TryGetBinding("HEAD", out _).Should().BeTrue();
    }

    [Fact]
    public void WithCacheIdentity_UrlSentinel_CreatesUrlBinding()
    {
        using WebApplication app = CreateApp();
        RouteHandlerBuilder builder = app.MapPost("/rpc", () => Results.Ok());

        builder.WithCacheIdentity(["POST"], CacheIdentities.Url);

        CacheIdentityEndpointMetadata meta = GetIdentity(app, builder);
        meta.TryGetBinding("POST", out CacheIdentityBinding? binding).Should().BeTrue();
        binding!.Kind.Should().Be(CacheIdentityKind.Url);
    }

    [Fact]
    public void WithCacheIdentity_DuplicateMethodAcrossCalls_Throws()
    {
        using WebApplication app = CreateApp();
        RouteHandlerBuilder builder = app.MapMethods(
            "/search",
            ["GET", "POST"],
            () => Results.Ok());

        builder.WithCacheIdentity(["GET", "HEAD"], "a");

        Action act = () =>
        {
            builder.WithCacheIdentity(["POST", "GET"], "b");
            _ = GetIdentity(app, builder);
        };

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*GET*");
    }

    [Fact]
    public void WithCacheIdentity_DuplicateMethodInSameCall_Throws()
    {
        using WebApplication app = CreateApp();
        RouteHandlerBuilder builder = app.MapGet("/x", () => Results.Ok());

        Action act = () => builder.WithCacheIdentity(["GET", "get"], "a");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void WithCacheIdentity_EmptyMethods_Throws()
    {
        using WebApplication app = CreateApp();
        RouteHandlerBuilder builder = app.MapGet("/x", () => Results.Ok());

        Action act = () => builder.WithCacheIdentity(Array.Empty<string>(), "a");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void WithContentHashCacheIdentity_AddsContentHashBinding()
    {
        using WebApplication app = CreateApp();
        RouteHandlerBuilder builder = app.MapPost("/graphql", () => Results.Ok());

        builder.WithContentHashCacheIdentity(["POST"], maxBodyBytes: 1024);

        CacheIdentityEndpointMetadata meta = GetIdentity(app, builder);
        meta.TryGetBinding("POST", out CacheIdentityBinding? binding).Should().BeTrue();
        binding!.Kind.Should().Be(CacheIdentityKind.ContentHash);
        binding.MaxBodyBytes.Should().Be(1024);
    }

    [Fact]
    public void WithContentHash_ThenNamed_SameMethod_Throws()
    {
        using WebApplication app = CreateApp();
        RouteHandlerBuilder builder = app.MapPost("/x", () => Results.Ok());

        builder.WithContentHashCacheIdentity(["POST"]);

        Action act = () =>
        {
            builder.WithCacheIdentity(["POST"], "a");
            _ = GetIdentity(app, builder);
        };

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*POST*");
    }

    [Fact]
    public void WithoutIdentityHelpers_EndpointHasNoIdentityMetadata()
    {
        using WebApplication app = CreateApp();
        RouteHandlerBuilder builder = app.MapGet("/catalog", () => Results.Ok());
        builder.CacheOutputWithDomain("catalog");

        Endpoint endpoint = GetEndpoint(app, builder);
        endpoint.Metadata.GetMetadata<CacheIdentityEndpointMetadata>().Should().BeNull();
    }

    private static WebApplication CreateApp()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        return builder.Build();
    }

    private static Endpoint GetEndpoint(WebApplication app, RouteHandlerBuilder _)
    {
        app.StartAsync().GetAwaiter().GetResult();
        try
        {
            return app.Services.GetRequiredService<EndpointDataSource>().Endpoints.Last();
        }
        finally
        {
            app.StopAsync().GetAwaiter().GetResult();
        }
    }

    private static CacheIdentityEndpointMetadata GetIdentity(WebApplication app, RouteHandlerBuilder builder)
    {
        Endpoint endpoint = GetEndpoint(app, builder);
        CacheIdentityEndpointMetadata? meta = endpoint.Metadata.GetMetadata<CacheIdentityEndpointMetadata>();
        meta.Should().NotBeNull();
        return meta;
    }
}
