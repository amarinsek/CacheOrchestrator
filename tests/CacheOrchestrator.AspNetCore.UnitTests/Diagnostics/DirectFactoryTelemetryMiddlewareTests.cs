using CacheOrchestrator.Admin;
using CacheOrchestrator.Configuration;
using CacheOrchestrator.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace CacheOrchestrator.AspNetCore.UnitTests.Diagnostics;

public class DirectFactoryTelemetryMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_DirectFactoryFailure_IsRecordedOnce()
    {
        IAdminStatsCollector admin = Substitute.For<IAdminStatsCollector>();
        admin.IsEnabled.Returns(true);
        ServiceProvider services = new ServiceCollection()
            .AddSingleton(admin)
            .BuildServiceProvider();
        var http = new DefaultHttpContext { RequestServices = services };
        http.Features.Set<ICacheOrchestratorFeature>(new CacheOrchestratorFeature
        {
            DomainOptions = new DomainHttpCacheOptions
            {
                CoreOptions = new DomainCacheOptions { Domain = "promotions" }
            }
        });
        var sut = new DirectFactoryTelemetryMiddleware(
            _ => throw new InvalidOperationException("boom"));

        Func<Task> act = () => sut.InvokeAsync(http);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("boom");
        admin.Received(1).RecordFactory(Arg.Is<AdminFactoryRecord>(record =>
            record.Domain == "promotions" && record.Failed));
    }

    [Fact]
    public async Task InvokeAsync_DataCacheWasObserved_DoesNotRecordDirectFailure()
    {
        IAdminStatsCollector admin = Substitute.For<IAdminStatsCollector>();
        admin.IsEnabled.Returns(true);
        ServiceProvider services = new ServiceCollection()
            .AddSingleton(admin)
            .BuildServiceProvider();
        var http = new DefaultHttpContext { RequestServices = services };
        http.Features.Set<ICacheOrchestratorFeature>(new CacheOrchestratorFeature
        {
            DomainOptions = new DomainHttpCacheOptions
            {
                CoreOptions = new DomainCacheOptions { Domain = "catalog" }
            }
        });
        CacheFactoryExecutionFeatureAccessor.GetOrCreate(http).DataCacheObserved = true;
        var sut = new DirectFactoryTelemetryMiddleware(
            _ => throw new InvalidOperationException("boom"));

        Func<Task> act = () => sut.InvokeAsync(http);

        await act.Should().ThrowAsync<InvalidOperationException>();
        admin.DidNotReceive().RecordFactory(Arg.Any<AdminFactoryRecord>());
    }
}
