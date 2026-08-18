using CacheOrchestrator.AdminConsole.Models;

namespace CacheOrchestrator.UnitTests.Admin;

public class AdminConsoleWriteValidatorsTests
{
    [Fact]
    public void Invalidate_DomainScope_RequiresDomain()
    {
        Action act = () => AdminConsoleWriteValidators.Validate(new AdminConsoleInvalidateRequest
        {
            Scope = "domain",
            Domain = " ",
            Target = "all",
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
            Target = "all",
        });
        act.Should().Throw<ArgumentException>().WithMessage("*EntityId*");
    }

    [Fact]
    public void Invalidate_BadTarget_Throws()
    {
        Action act = () => AdminConsoleWriteValidators.Validate(new AdminConsoleInvalidateRequest
        {
            Scope = "domain",
            Domain = "catalog",
            Target = "peer:x",
        });
        act.Should().Throw<ArgumentException>().WithMessage("*Target*");
    }

    [Fact]
    public void Invalidate_ValidDomain_DoesNotThrow()
    {
        Action act = () => AdminConsoleWriteValidators.Validate(new AdminConsoleInvalidateRequest
        {
            Scope = "domain",
            Domain = "catalog",
            Target = "instance:app-1",
        });
        act.Should().NotThrow();
    }

    [Fact]
    public void Ttl_RequiresAtLeastOneField()
    {
        Action act = () => AdminConsoleWriteValidators.Validate(new AdminConsoleTtlPatchRequest
        {
            Target = "all",
        });
        act.Should().Throw<ArgumentException>().WithMessage("*TTL*");
    }

    [Fact]
    public void Ttl_RejectsNegative()
    {
        Action act = () => AdminConsoleWriteValidators.Validate(new AdminConsoleTtlPatchRequest
        {
            Target = "all",
            OutputCacheTtlSeconds = -1,
        });
        act.Should().Throw<ArgumentException>().WithMessage("*OutputCacheTtlSeconds*");
    }

    [Fact]
    public void Version_ValidTarget_DoesNotThrow()
    {
        Action act = () => AdminConsoleWriteValidators.Validate(new AdminConsoleVersionRequest
        {
            Version = "bump",
            Target = "all",
        });
        act.Should().NotThrow();
    }
}
