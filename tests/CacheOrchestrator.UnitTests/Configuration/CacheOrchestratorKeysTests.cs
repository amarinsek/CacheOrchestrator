using CacheOrchestrator.Configuration;

namespace CacheOrchestrator.UnitTests.Configuration;

public class CacheOrchestratorKeysTests
{
    [Fact]
    public void DispositionKey_IsNotNull() => CacheOrchestratorKeys.DispositionKey.Should().NotBeNull();

    [Fact]
    public void DispositionKey_IsStableReference()
    {
        object a = CacheOrchestratorKeys.DispositionKey;
        object b = CacheOrchestratorKeys.DispositionKey;

        a.Should().BeSameAs(b);
    }
}