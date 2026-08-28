using CacheOrchestrator.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CacheOrchestrator.Core.UnitTests.Configuration;

public class CacheOrchestratorOptionsValidatorTests
{
    private readonly CacheOrchestratorOptionsValidator _sut = new();

    [Fact]
    public void Validate_DefaultValidOptions_ReturnsSuccess()
    {
        ValidateOptionsResult result = _sut.Validate(null, CreateValidOptions());

        result.Succeeded.Should().BeTrue();
        result.Failures.Should().BeNullOrEmpty();
    }

    [Fact]
    public void Validate_MultipleInMemoryInstances_ReturnsSuccess()
    {
        CacheOrchestratorOptions options = CreateValidOptions();
        options.DataCacheInstances["secondary"] = new CacheOrchestratorOptions.DataCacheInstanceOptions
        {
            Provider = "InMemory"
        };

        ValidateOptionsResult result = _sut.Validate(null, options);

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validate_RedisProviderName_IsAcceptedByCoreValidator()
    {
        // Connection string validation lives in CacheOrchestrator.Redis package.
        CacheOrchestratorOptions options = CreateValidOptions();
        options.DataCacheInstances["default"] = new CacheOrchestratorOptions.DataCacheInstanceOptions
        {
            Provider = "Redis"
        };

        ValidateOptionsResult result = _sut.Validate(null, options);

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validate_DomainWithKnownInstanceReference_ReturnsSuccess()
    {
        CacheOrchestratorOptions options = CreateValidOptions();
        options.DataCacheInstances["pii"] = new CacheOrchestratorOptions.DataCacheInstanceOptions
        {
            Provider = "InMemory"
        };
        options.Domains["users"] = new CacheOrchestratorOptions.DomainCacheSettings
        {
            DataCache = new() { Instance = "pii" }
        };

        ValidateOptionsResult result = _sut.Validate(null, options);

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validate_CustomBackendProvider_ReturnsSuccess()
    {
        CacheOrchestratorOptions options = CreateValidOptions();
        options.DataCacheInstances["default"] = new CacheOrchestratorOptions.DataCacheInstanceOptions
        {
            Provider = "SqlServer"
        };

        ValidateOptionsResult result = _sut.Validate(null, options);

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validate_EmptyDataCacheInstances_Fails()
    {
        CacheOrchestratorOptions options = CreateValidOptions();
        options.DataCacheInstances.Clear();

        ValidateOptionsResult result = _sut.Validate(null, options);

        result.Succeeded.Should().BeFalse();
        result.Failures.Should().Contain(f => f.Contains("default", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_MissingDefaultKey_Fails()
    {
        CacheOrchestratorOptions options = CreateValidOptions();
        options.DataCacheInstances.Clear();
        options.DataCacheInstances["secondary"] = new CacheOrchestratorOptions.DataCacheInstanceOptions
        {
            Provider = "InMemory"
        };

        ValidateOptionsResult result = _sut.Validate(null, options);

        result.Succeeded.Should().BeFalse();
        result.Failures.Should().Contain(f => f.Contains("default", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_BlankDataCacheInstanceProvider_Fails(string? provider)
    {
        CacheOrchestratorOptions options = CreateValidOptions();
        options.DataCacheInstances["default"] = new CacheOrchestratorOptions.DataCacheInstanceOptions
        {
            Provider = provider!
        };

        ValidateOptionsResult result = _sut.Validate(null, options);

        result.Succeeded.Should().BeFalse();
        result.Failures.Should().Contain(f =>
            f.Contains("DataCacheInstances", StringComparison.OrdinalIgnoreCase) &&
            f.Contains("Provider", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("Memory")]
    [InlineData("UnknownDB")]
    public void Validate_UnknownDataCacheInstanceProvider_IsDeferredToDataCachePackage(string provider)
    {
        // AspNet Output Cache registrars no longer constrain DataCacheInstances providers;
        // Fusion/Hybrid resolve backends when registered.
        CacheOrchestratorOptions options = CreateValidOptions();
        options.DataCacheInstances["default"] = new CacheOrchestratorOptions.DataCacheInstanceOptions
        {
            Provider = provider
        };

        ValidateOptionsResult result = _sut.Validate(null, options);

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validate_DomainReferencesUnknownInstance_Fails()
    {
        CacheOrchestratorOptions options = CreateValidOptions();
        options.Domains["products"] = new CacheOrchestratorOptions.DomainCacheSettings
        {
            DataCache = new() { Instance = "nonexistent" }
        };

        ValidateOptionsResult result = _sut.Validate(null, options);

        result.Succeeded.Should().BeFalse();
        result.Failures.Should().Contain(f =>
            f.Contains("products", StringComparison.OrdinalIgnoreCase) &&
            f.Contains("nonexistent", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_DomainWithNullDataCacheInstance_ReturnsSuccess()
    {
        CacheOrchestratorOptions options = CreateValidOptions();
        options.Domains["products"] = new CacheOrchestratorOptions.DomainCacheSettings
        {
            DataCache = new() { Instance = null }
        };

        ValidateOptionsResult result = _sut.Validate(null, options);

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validate_NegativeDomainDefaults_DataCacheTtl_Fails()
    {
        CacheOrchestratorOptions options = CreateValidOptions();
        options.DomainDefaults.DataCache = new() { TtlSeconds = -5 };

        ValidateOptionsResult result = _sut.Validate(null, options);

        result.Succeeded.Should().BeFalse();
        result.Failures.Should().Contain(f => f.Contains("dataCache.ttlSeconds", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_ZeroTtls_AreAllowed()
    {
        CacheOrchestratorOptions options = CreateValidOptions();
        options.DomainDefaults.DataCache = new() { TtlSeconds = 0 };

        ValidateOptionsResult result = _sut.Validate(null, options);

        result.Succeeded.Should().BeTrue();
    }

    [Theory]
    [InlineData("products")]
    [InlineData("product-detail")]
    [InlineData("reports:v1")]
    [InlineData("user_profile")]
    [InlineData("tenant@acme")]
    public void Validate_NormalizedDomainKey_ReturnsSuccess(string domain)
    {
        CacheOrchestratorOptions options = CreateValidOptions();
        options.Domains[domain] = new CacheOrchestratorOptions.DomainCacheSettings();

        ValidateOptionsResult result = _sut.Validate(null, options);

        result.Succeeded.Should().BeTrue();
    }

    [Theory]
    [InlineData("Products")]
    [InlineData("MyStore")]
    [InlineData("PRODUCTS")]
    public void Validate_CaseOnlyDomainKey_ReturnsSuccessAndLogsWarning(string domain)
    {
        ILogger logger = Substitute.For<ILogger>();
        logger.IsEnabled(LogLevel.Warning).Returns(true);
        var sut = new CacheOrchestratorOptionsValidator(logger);
        CacheOrchestratorOptions options = CreateValidOptions();
        options.Domains[domain] = new CacheOrchestratorOptions.DomainCacheSettings();

        ValidateOptionsResult result = sut.Validate(null, options);

        result.Succeeded.Should().BeTrue();
        logger.Received().Log(
            LogLevel.Warning,
            Arg.Any<EventId>(),
            Arg.Is<object>(state => state.ToString()!.Contains(domain, StringComparison.Ordinal)),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Theory]
    [InlineData("My Store", "my-store")]
    [InlineData("foo--bar", "foo-bar")]
    [InlineData("products!", "products")]
    public void Validate_DomainKeyThatChangesBeyondCase_Fails(string domain, string normalized)
    {
        CacheOrchestratorOptions options = CreateValidOptions();
        options.Domains[domain] = new CacheOrchestratorOptions.DomainCacheSettings();

        ValidateOptionsResult result = _sut.Validate(null, options);

        result.Succeeded.Should().BeFalse();
        result.Failures.Should().Contain(f =>
            f.Contains(domain, StringComparison.Ordinal) &&
            f.Contains(normalized, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("!!!")]
    [InlineData("---")]
    public void Validate_DomainKeyThatNormalizesToDefault_Fails(string domain)
    {
        CacheOrchestratorOptions options = CreateValidOptions();
        options.Domains[domain] = new CacheOrchestratorOptions.DomainCacheSettings();

        ValidateOptionsResult result = _sut.Validate(null, options);

        result.Succeeded.Should().BeFalse();
        result.Failures.Should().Contain(f =>
            f.Contains(domain, StringComparison.Ordinal) &&
            f.Contains("default", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_WhitespaceDomainKey_Fails()
    {
        CacheOrchestratorOptions options = CreateValidOptions();
        options.Domains["   "] = new CacheOrchestratorOptions.DomainCacheSettings();

        ValidateOptionsResult result = _sut.Validate(null, options);

        result.Succeeded.Should().BeFalse();
        result.Failures.Should().Contain(f => f.Contains("null or whitespace", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_DefaultDomainKey_ReturnsSuccess()
    {
        CacheOrchestratorOptions options = CreateValidOptions();
        options.Domains["default"] = new CacheOrchestratorOptions.DomainCacheSettings();

        ValidateOptionsResult result = _sut.Validate(null, options);

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void ValidateAllowlist_WhenEmptyAndNotAllowed_Fails()
    {
        List<string> failures = [];
        CacheOrchestratorOptionsValidator.ValidateAllowlist(
            "Domain 'x'",
            "RequiredList",
            [],
            max: 8,
            failures,
            allowEmpty: false);

        failures.Should().ContainSingle(f => f.Contains("must not be empty", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ValidateAllowlist_WhenEmptyAndAllowed_Succeeds()
    {
        List<string> failures = [];
        CacheOrchestratorOptionsValidator.ValidateAllowlist(
            "Domain 'x'",
            "OptionalList",
            [],
            max: 8,
            failures,
            allowEmpty: true);

        failures.Should().BeEmpty();
    }

    private static CacheOrchestratorOptions CreateValidOptions() =>
        new()
        {
            DataCacheInstances = new Dictionary<string, CacheOrchestratorOptions.DataCacheInstanceOptions>(
                StringComparer.OrdinalIgnoreCase)
            {
                ["default"] = new CacheOrchestratorOptions.DataCacheInstanceOptions { Provider = "InMemory" }
            }
        };
}
