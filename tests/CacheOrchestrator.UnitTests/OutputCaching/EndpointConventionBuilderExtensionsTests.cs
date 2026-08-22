using CacheOrchestrator.OutputCache;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace CacheOrchestrator.UnitTests.OutputCaching;

public class EndpointConventionBuilderExtensionsTests
{
    // =========================
    // CacheOutputWithDomain(string)
    // =========================

    [Fact]
    public void CacheOutputWithDomain_WithString_AddsDomainOutputCachePolicy()
    {
        using var app = CreateApp();
        var builder = app.MapGet("/products", () => Results.Ok());

        builder.CacheOutputWithDomain("products");

        var endpoint = GetEndpoint(app, builder);
        endpoint.Metadata.OfType<DomainOutputCachePolicy>().Should().ContainSingle();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void CacheOutputWithDomain_WithInvalidString_Throws(string? domain)
    {
        using var app = CreateApp();
        var builder = app.MapGet("/products", () => Results.Ok());

        var act = () => builder.CacheOutputWithDomain(domain!);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void CacheOutputWithDomain_WithDomainAndEntityKind_AddsKindScopedPolicy()
    {
        using var app = CreateApp();
        var builder = app.MapGet("/products", () => Results.Ok());

        builder.CacheOutputWithDomain("store", "products");

        DomainOutputCachePolicy policy = GetEndpoint(app, builder).Metadata.OfType<DomainOutputCachePolicy>().Single();
        policy.EntityKind.Should().Be("products");
        policy.ResourceRouteKey.Should().BeNull();
    }

    // =========================
    // CacheOutputWithDomain(Func)
    // =========================

    [Fact]
    public void CacheOutputWithDomain_WithResolver_AddsDomainOutputCachePolicy()
    {
        using var app = CreateApp();
        var builder = app.MapGet("/products", () => Results.Ok());

        builder.CacheOutputWithDomain(ctx => "dynamic-domain");

        var endpoint = GetEndpoint(app, builder);
        endpoint.Metadata.OfType<DomainOutputCachePolicy>().Should().ContainSingle();
    }

    [Fact]
    public void CacheOutputWithDomain_WithNullResolver_Throws()
    {
        using var app = CreateApp();
        var builder = app.MapGet("/products", () => Results.Ok());

        var act = () => builder.CacheOutputWithDomain((Func<HttpContext, string>)null!);

        act.Should().Throw<ArgumentNullException>();
    }

    // =========================
    // CacheOutputWithDomainTemplate
    // =========================

    [Fact]
    public void CacheOutputWithDomainTemplate_AddsDomainOutputCachePolicy()
    {
        using var app = CreateApp();
        var builder = app.MapGet("/products", () => Results.Ok());

        builder.CacheOutputWithDomainTemplate("tenant-{host}");

        var endpoint = GetEndpoint(app, builder);
        endpoint.Metadata.OfType<DomainOutputCachePolicy>().Should().ContainSingle();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void CacheOutputWithDomainTemplate_WithInvalidTemplate_Throws(string? template)
    {
        using var app = CreateApp();
        var builder = app.MapGet("/products", () => Results.Ok());

        var act = () => builder.CacheOutputWithDomainTemplate(template!);

        act.Should().Throw<ArgumentException>();
    }

    // =========================
    // CacheOutputWithDomainAttribute
    // =========================

    [Fact]
    public void CacheOutputWithDomainAttribute_WhenAttributePresent_AddsPolicy()
    {
        var endpointBuilder = new TestEndpointBuilder();
        endpointBuilder.Metadata.Add(new CacheDomainAttribute("products"));

        var conventionBuilder = new TestConventionBuilder();
        conventionBuilder.CacheOutputWithDomainAttribute();

        // Apply recorded conventions
        foreach (var convention in conventionBuilder.Conventions)
            convention(endpointBuilder);

        endpointBuilder.Metadata.OfType<DomainOutputCachePolicy>().Should().ContainSingle();
    }

    [Fact]
    public void CacheOutputWithDomainAttribute_WhenAttributeMissing_DoesNotAddPolicy()
    {
        var endpointBuilder = new TestEndpointBuilder();
        var conventionBuilder = new TestConventionBuilder();
        conventionBuilder.CacheOutputWithDomainAttribute();

        foreach (var convention in conventionBuilder.Conventions)
            convention(endpointBuilder);

        endpointBuilder.Metadata.OfType<DomainOutputCachePolicy>().Should().BeEmpty();
    }

    // =========================
    // Helpers
    // =========================

    private static WebApplication CreateApp()
    {
        var builder = WebApplication.CreateBuilder();
        return builder.Build();
    }

    private static Endpoint GetEndpoint(WebApplication app, RouteHandlerBuilder routeBuilder)
    {
        // Force endpoint finalization
        app.StartAsync().GetAwaiter().GetResult();
        try
        {
            var endpointDataSource = app.Services.GetRequiredService<EndpointDataSource>();
            return endpointDataSource.Endpoints.Last();
        }
        finally
        {
            app.StopAsync().GetAwaiter().GetResult();
        }
    }

    private sealed class TestConventionBuilder : IEndpointConventionBuilder
    {
        public List<Action<EndpointBuilder>> Conventions { get; } = [];

        public void Add(Action<EndpointBuilder> convention) => Conventions.Add(convention);
    }

    private sealed class TestEndpointBuilder : EndpointBuilder
    {
        public override Endpoint Build() => new(
            _ => Task.CompletedTask,
            new EndpointMetadataCollection(Metadata),
            "test");
    }
}