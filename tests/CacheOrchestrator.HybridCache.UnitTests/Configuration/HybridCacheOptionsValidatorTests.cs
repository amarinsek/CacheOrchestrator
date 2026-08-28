using CacheOrchestrator.Configuration;

namespace CacheOrchestrator.HybridCache.UnitTests.Configuration;

public class HybridCacheOptionsValidatorTests
{
    [Fact]
    public void Validate_DefaultInstanceOnly_Succeeds()
    {
        var validator = new CacheOrchestrator.HybridCache.HybridCacheOptionsValidator();

        validator.Validate(null, new CacheOrchestratorOptions()).Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validate_NamedInstance_FailsClearly()
    {
        var options = new CacheOrchestratorOptions();
        options.DataCacheInstances["pii"] = new();
        var validator = new CacheOrchestrator.HybridCache.HybridCacheOptionsValidator();

        var result = validator.Validate(null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("pii").And.Contain("does not support named Data Cache");
    }
}
