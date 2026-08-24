using System.Text.Json;
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
    public void Version_DoesNotThrow()
    {
        Action act = () => AdminConsoleWriteValidators.Validate(new AdminConsoleVersionRequest
        {
            Version = "bump",
        });
        act.Should().NotThrow();
    }

    [Fact]
    public void Invalidate_UnknownScope_Throws()
    {
        Action act = () => AdminConsoleWriteValidators.Validate(new AdminConsoleInvalidateRequest
        {
            Scope = "everything",
            Domain = "catalog",
        });
        act.Should().Throw<ArgumentException>().WithMessage("*domain, entity, entityKind, tags*");
    }

    [Fact]
    public void Invalidate_EntityKindScope_RequiresKind()
    {
        Action act = () => AdminConsoleWriteValidators.Validate(new AdminConsoleInvalidateRequest
        {
            Scope = "entityKind",
            Domain = "catalog",
        });
        act.Should().Throw<ArgumentException>().WithMessage("*EntityKind*");
    }

    [Fact]
    public void Invalidate_TagsScope_RequiresNonEmptyTags()
    {
        Action act = () => AdminConsoleWriteValidators.Validate(new AdminConsoleInvalidateRequest
        {
            Scope = "tags",
            Tags = ["  ", ""],
        });
        act.Should().Throw<ArgumentException>().WithMessage("*Tags*");
    }

    [Fact]
    public void Invalidate_TagsScope_Valid_DoesNotThrow()
    {
        Action act = () => AdminConsoleWriteValidators.Validate(new AdminConsoleInvalidateRequest
        {
            Scope = "tags",
            Tags = ["domain:catalog"],
        });
        act.Should().NotThrow();
    }

    [Fact]
    public void Settings_RequiresAtLeastOneSetting()
    {
        Action act = () => AdminConsoleWriteValidators.Validate(new AdminConsoleSettingsPatchRequest
        {
            Settings = [],
        });
        act.Should().Throw<ArgumentException>().WithMessage("*setting*");
    }

    [Fact]
    public void Settings_Valid_DoesNotThrow()
    {
        Action act = () => AdminConsoleWriteValidators.Validate(new AdminConsoleSettingsPatchRequest
        {
            Settings = new Dictionary<string, JsonElement>
            {
                ["outputCacheTtlSeconds"] = JsonSerializer.SerializeToElement(60),
            },
        });
        act.Should().NotThrow();
    }

    [Fact]
    public void Version_NullRequest_Throws()
    {
        Action act = () => AdminConsoleWriteValidators.Validate((AdminConsoleVersionRequest)null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
