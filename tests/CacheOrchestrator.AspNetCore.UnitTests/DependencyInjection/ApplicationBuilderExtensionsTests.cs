using CacheOrchestrator.DependencyInjection;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace CacheOrchestrator.AspNetCore.UnitTests.DependencyInjection;

public class ApplicationBuilderExtensionsTests
{
    [Fact]
    public void UseCacheOrchestrator_ReturnsSameApplicationBuilder()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.Services.AddOutputCache();
        WebApplication app = builder.Build();

        IApplicationBuilder result = app.UseCacheOrchestrator();

        result.Should().BeSameAs(app);
    }

    [Fact]
    public void UseCacheOrchestrator_DoesNotThrow_WhenOutputCacheIsRegistered()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.Services.AddOutputCache();
        WebApplication app = builder.Build();

        Func<IApplicationBuilder> act = () => app.UseCacheOrchestrator();

        act.Should().NotThrow();
    }

    [Fact]
    public void UseCacheOrchestrator_CanBeChained()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.Services.AddOutputCache();
        WebApplication app = builder.Build();

        Func<IApplicationBuilder> act = () => app
            .UseCacheOrchestrator()
            .Use(async (ctx, next) => await next());

        act.Should().NotThrow();
    }
}
