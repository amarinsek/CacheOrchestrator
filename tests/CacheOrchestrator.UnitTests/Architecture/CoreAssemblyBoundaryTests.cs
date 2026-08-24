using System.Reflection;

namespace CacheOrchestrator.UnitTests.Architecture;

public class CoreAssemblyBoundaryTests
{
    [Fact]
    public void Core_DoesNotReference_AspNetCore_Or_FusionCache()
    {
        Assembly core = typeof(CacheOrchestrator.Configuration.DomainName).Assembly;
        string[] referenced = core.GetReferencedAssemblies().Select(a => a.Name!).ToArray();

        referenced.Should().NotContain(n =>
            n.Equals("Microsoft.AspNetCore", StringComparison.OrdinalIgnoreCase)
            || n.StartsWith("Microsoft.AspNetCore.", StringComparison.OrdinalIgnoreCase));

        referenced.Should().NotContain(n =>
            n.Equals("ZiggyCreatures.Caching.Fusion", StringComparison.OrdinalIgnoreCase)
            || n.StartsWith("ZiggyCreatures.Caching.Fusion", StringComparison.OrdinalIgnoreCase));
    }
}
