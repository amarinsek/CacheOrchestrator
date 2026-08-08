using CacheOrchestrator.Configuration;

namespace CacheOrchestrator.UnitTests.Configuration;

public class DistributedResilienceOptionsTests
{
    [Fact]
    public void IsFactoryDefault_WhenDefaults_IsTrue()
    {
        new DistributedResilienceOptions().IsFactoryDefault.Should().BeTrue();
    }

    [Fact]
    public void IsFactoryDefault_WhenCustom_IsFalse()
    {
        new DistributedResilienceOptions { SoftTimeoutSeconds = 9 }.IsFactoryDefault.Should().BeFalse();
    }
}
