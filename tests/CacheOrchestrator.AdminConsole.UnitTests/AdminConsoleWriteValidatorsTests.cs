using CacheOrchestrator.AdminConsole.Models;

namespace CacheOrchestrator.AdminConsole.UnitTests;

public class AdminConsoleWriteValidatorsTests
{
    [Fact]
    public void Invalidate_DomainScope_RequiresDomain()
    {
        Action act = () => AdminConsoleWriteValidators.Validate(new AdminConsoleInvalidateRequest
        {
            Scope = "domain",
            Domain = " ",
        });
        act.Should().Throw<ArgumentException>().WithMessage("*Domain*");
    }

    [Fact]
    public void Invalidate_EntityScope_RequiresKindAndId()
    {
        Action act = () => AdminConsoleWriteValidators.Validate(new AdminConsoleInvalidateRequest
        {
            Scope = "entity",
            Domain = "catalog",
            EntityKind = "Product",
        });
        act.Should().Throw<ArgumentException>().WithMessage("*EntityId*");
    }

    [Fact]
    public void Invalidate_ValidDomain_DoesNotThrow()
    {
        Action act = () => AdminConsoleWriteValidators.Validate(new AdminConsoleInvalidateRequest
        {
            Scope = "domain",
            Domain = "catalog",
        });
        act.Should().NotThrow();
    }

    [Fact]
    public void Ttl_RequiresAtLeastOneField()
    {
        Action act = () => AdminConsoleWriteValidators.Validate(new AdminConsoleTtlPatchRequest());
        act.Should().Throw<ArgumentException>().WithMessage("*TTL*");
    }

    [Fact]
    public void Ttl_RejectsNegative()
    {
        Action act = () => AdminConsoleWriteValidators.Validate(new AdminConsoleTtlPatchRequest
        {
            OutputCacheTtlSeconds = -1,
        });
        act.Should().Throw<ArgumentException>().WithMessage("*OutputCacheTtlSeconds*");
    }

    [Fact]
    public void Version_DoesNotThrow()
    {
        Action act = () => AdminConsoleWriteValidators.Validate(new AdminConsoleVersionRequest
        {
            Version = "bump",
        });
        act.Should().NotThrow();
    }
}
