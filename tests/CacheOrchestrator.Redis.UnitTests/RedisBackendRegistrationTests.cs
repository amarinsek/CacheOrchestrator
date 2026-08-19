using CacheOrchestrator.DependencyInjection;
using CacheOrchestrator.Redis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CacheOrchestrator.Redis.UnitTests;

public class RedisBackendRegistrationTests
{
    [Fact]
    public void AddCacheOrchestrator_RedisWithoutConnectionString_ThrowsAtRegistration()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cache:OutputCache:Provider"] = "Redis",
                ["Cache:FusionCacheInstances:default:Provider"] = "Redis"
            })
            .Build();

        services.AddLogging();

        var act = () => services.AddCacheOrchestrator(config, o => o.AddRedisBackend());

        act.Should().Throw<InvalidOperationException>()
           .WithMessage("*Redis*");
    }
}
