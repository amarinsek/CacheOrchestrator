using CacheOrchestrator.Identity;

namespace CacheOrchestrator.AspNetCore.UnitTests.Identity;

public class CacheIdentityMaterialTests
{
    [Fact]
    public void DictionaryConstructor_CopiesCallerOwnedValues()
    {
        Dictionary<string, string> values = new(StringComparer.Ordinal)
        {
            ["tenant"] = "north"
        };

        CacheIdentityMaterial material = new(values);
        values["tenant"] = "south";

        material.Values["tenant"].Should().Be("north");
    }
}
