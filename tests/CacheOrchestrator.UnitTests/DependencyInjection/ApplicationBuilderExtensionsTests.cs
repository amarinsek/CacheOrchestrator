using CacheOrchestrator.DependencyInjection;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace CacheOrchestrator.UnitTests.DependencyInjection;

public class ApplicationBuilderExtensionsTests
{
    [Fact]
    public void UseCacheOrchestrator_ReturnsSameApplicationBuilder()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddOutputCache();
        var app = builder.Build();

        var result = app.UseCacheOrchestrator();

        result.Should().BeSameAs(app);
    }

    [Fact]
    public void UseCacheOrchestrator_DoesNotThrow_WhenOutputCacheIsRegistered()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddOutputCache();
        var app = builder.Build();

        var act = () => app.UseCacheOrchestrator();

        act.Should().NotThrow();
    }

    [Fact]
    public void UseCacheOrchestrator_CanBeChained()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddOutputCache();
        var app = builder.Build();

        var act = () => app
            .UseCacheOrchestrator()
            .Use(async (ctx, next) => await next());

        act.Should().NotThrow();
    }
}