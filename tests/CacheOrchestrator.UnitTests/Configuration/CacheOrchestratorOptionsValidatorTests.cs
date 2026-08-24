using CacheOrchestrator.Configuration;
using Microsoft.Extensions.Logging;

namespace CacheOrchestrator.UnitTests.Configuration;

public class CacheOrchestratorOptionsValidatorTests
{
    private readonly CacheOrchestratorOptionsValidator _sut = new(["InMemory", "Redis", "SqlServer"]);

    [Fact]
    public void Validate_DefaultValidOptions_ReturnsSuccess()
    {
        var result = _sut.Validate(null, CreateValidOptions());

        result.Succeeded.Should().BeTrue();
        result.Failures.Should().BeNullOrEmpty();
    }

    [Fact]
    public void Validate_MultipleInMemoryInstances_ReturnsSuccess()
    {
        var options = CreateValidOptions();
        options.FusionCacheInstances["secondary"] = new CacheOrchestratorOptions.FusionCacheInstanceOptions
        {
            Provider = "InMemory"
        };

        var result = _sut.Validate(null, options);

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validate_RedisProviderName_IsAcceptedByCoreValidator()
    {
        // Connection string validation lives in CacheOrchestrator.Redis package.
        var options = CreateValidOptions();
        options.OutputCache.Provider = "Redis";
        options.FusionCacheInstances["default"] = new CacheOrchestratorOptions.FusionCacheInstanceOptions
        {
            Provider = "Redis"
        };

        var result = _sut.Validate(null, options);

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validate_DomainWithKnownInstanceReference_ReturnsSuccess()
    {
        var options = CreateValidOptions();
        options.FusionCacheInstances["pii"] = new CacheOrchestratorOptions.FusionCacheInstanceOptions
        {
            Provider = "InMemory"
        };
        options.Domains["users"] = new CacheOrchestratorOptions.DomainCacheSettings
        {
            DataCache = new() { Instance = "pii" }
        };

        var result = _sut.Validate(null, options);

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validate_CustomBackendProvider_ReturnsSuccess()
    {
        var options = CreateValidOptions();
        options.OutputCache.Provider = "SqlServer";
        options.FusionCacheInstances["default"] = new CacheOrchestratorOptions.FusionCacheInstanceOptions
        {
            Provider = "SqlServer"
        };

        var result = _sut.Validate(null, options);

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validate_EmptyFusionCacheInstances_Fails()
    {
        var options = CreateValidOptions();
        options.FusionCacheInstances.Clear();

        var result = _sut.Validate(null, options);

        result.Succeeded.Should().BeFalse();
        result.Failures.Should().Contain(f => f.Contains("default", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_MissingDefaultKey_Fails()
    {
        var options = CreateValidOptions();
        options.FusionCacheInstances.Clear();
        options.FusionCacheInstances["secondary"] = new CacheOrchestratorOptions.FusionCacheInstanceOptions
        {
            Provider = "InMemory"
        };

        var result = _sut.Validate(null, options);

        result.Succeeded.Should().BeFalse();
        result.Failures.Should().Contain(f => f.Contains("default", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("Memory")]
    [InlineData("UnknownDB")]
    public void Validate_InvalidOutputCacheProvider_Fails(string? provider)
    {
        var options = CreateValidOptions();
        options.OutputCache.Provider = provider!;

        var result = _sut.Validate(null, options);

        result.Succeeded.Should().BeFalse();
        result.Failures.Should().Contain(f => f.Contains("OutputCache.Provider", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Memory")]
    [InlineData("UnknownDB")]
    public void Validate_InvalidFusionInstanceProvider_Fails(string? provider)
    {
        var options = CreateValidOptions();
        options.FusionCacheInstances["default"] = new CacheOrchestratorOptions.FusionCacheInstanceOptions
        {
            Provider = provider!
        };

        var result = _sut.Validate(null, options);

        result.Succeeded.Should().BeFalse();
        result.Failures.Should().Contain(f =>
            f.Contains("FusionCacheInstances", StringComparison.OrdinalIgnoreCase) &&
            f.Contains("Provider", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_DomainReferencesUnknownInstance_Fails()
    {
        var options = CreateValidOptions();
        options.Domains["products"] = new CacheOrchestratorOptions.DomainCacheSettings
        {
            DataCache = new() { Instance = "nonexistent" }
        };

        var result = _sut.Validate(null, options);

        result.Succeeded.Should().BeFalse();
        result.Failures.Should().Contain(f =>
            f.Contains("products", StringComparison.OrdinalIgnoreCase) &&
            f.Contains("nonexistent", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_DomainWithNullFusionCacheInstance_ReturnsSuccess()
    {
        var options = CreateValidOptions();
        options.Domains["products"] = new CacheOrchestratorOptions.DomainCacheSettings
        {
            DataCache = new() { Instance = null }
        };

        var result = _sut.Validate(null, options);

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validate_NegativeDomainDefaults_OutputCacheTtl_Fails()
    {
        var options = CreateValidOptions();
        options.DomainDefaults.OutputCache = new() { Ttl = TimeSpan.FromSeconds(-1) };

        var result = _sut.Validate(null, options);

        result.Succeeded.Should().BeFalse();
        result.Failures.Should().Contain(f => f.Contains("OutputCache.Ttl", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_NegativeDomainDefaults_FusionCacheSoftTtl_Fails()
    {
        var options = CreateValidOptions();
        options.DomainDefaults.DataCache = new() { Ttl = TimeSpan.FromSeconds(-5) };

        var result = _sut.Validate(null, options);

        result.Succeeded.Should().BeFalse();
        result.Failures.Should().Contain(f => f.Contains("DataCache.Ttl", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_NegativeDomainDefaults_FusionCacheHardTtl_Fails()
    {
        var options = CreateValidOptions();
        options.DomainDefaults.FusionCache = new() { HardTtl = TimeSpan.FromSeconds(-10) };

        var result = _sut.Validate(null, options);

        result.Succeeded.Should().BeFalse();
        result.Failures.Should().Contain(f => f.Contains("FusionCache.HardTtl", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_NegativeDomainSpecific_OutputCacheTtl_Fails()
    {
        var options = CreateValidOptions();
        options.Domains["products"] = new CacheOrchestratorOptions.DomainCacheSettings
        {
            OutputCache = new() { Ttl = TimeSpan.FromSeconds(-3) }
        };

        var result = _sut.Validate(null, options);

        result.Succeeded.Should().BeFalse();
        result.Failures.Should().Contain(f =>
            f.Contains("products", StringComparison.OrdinalIgnoreCase) &&
            f.Contains("OutputCache.Ttl", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_ZeroTtls_AreAllowed()
    {
        var options = CreateValidOptions();
        options.DomainDefaults.OutputCache = new() { Ttl = TimeSpan.Zero };
        options.DomainDefaults.DataCache = new() { Ttl = TimeSpan.Zero };
        options.DomainDefaults.ClientCache = new() { Ttl = TimeSpan.Zero };
        options.DomainDefaults.FusionCache = new()
        {
            HardTtl = TimeSpan.Zero,
            EagerRefreshRatio = 0,
        };

        var result = _sut.Validate(null, options);

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validate_NegativeClientTtl_Fails()
    {
        var options = CreateValidOptions();
        options.DomainDefaults.ClientCache = new() { Ttl = TimeSpan.FromSeconds(-1) };

        var result = _sut.Validate(null, options);

        result.Succeeded.Should().BeFalse();
        result.Failures.Should().Contain(f => f.Contains("ClientCache.Ttl", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_NegativeFailSafe_Fails()
    {
        var options = CreateValidOptions();
        options.DomainDefaults.FusionCache = new() { FailSafe = TimeSpan.FromSeconds(-1) };

        var result = _sut.Validate(null, options);

        result.Succeeded.Should().BeFalse();
        result.Failures.Should().Contain(f => f.Contains("FusionCache.FailSafe", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_EagerRefreshRatio_One_Fails()
    {
        var options = CreateValidOptions();
        options.DomainDefaults.FusionCache = new() { EagerRefreshRatio = 1.0 };

        var result = _sut.Validate(null, options);

        result.Succeeded.Should().BeFalse();
        result.Failures.Should().Contain(f => f.Contains("FusionCache.EagerRefreshRatio", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_EmptyVaryByHeaders_Succeeds()
    {
        var options = CreateValidOptions();
        options.DomainDefaults.VaryByHeaders = [];

        var result = _sut.Validate(null, options);

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validate_WhitespaceVaryByHeadersEntry_Fails()
    {
        var options = CreateValidOptions();
        options.DomainDefaults.VaryByHeaders = ["Accept", "  "];

        var result = _sut.Validate(null, options);

        result.Succeeded.Should().BeFalse();
        result.Failures.Should().Contain(f => f.Contains("VaryByHeaders[1]", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("products")]
    [InlineData("product-detail")]
    [InlineData("reports:v1")]
    [InlineData("user_profile")]
    [InlineData("tenant@acme")]
    public void Validate_NormalizedDomainKey_ReturnsSuccess(string domain)
    {
        var options = CreateValidOptions();
        options.Domains[domain] = new CacheOrchestratorOptions.DomainCacheSettings();

        var result = _sut.Validate(null, options);

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
        var sut = new CacheOrchestratorOptionsValidator(["InMemory", "Redis", "SqlServer"], logger: logger);
        var options = CreateValidOptions();
        options.Domains[domain] = new CacheOrchestratorOptions.DomainCacheSettings();

        var result = sut.Validate(null, options);

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
        var options = CreateValidOptions();
        options.Domains[domain] = new CacheOrchestratorOptions.DomainCacheSettings();

        var result = _sut.Validate(null, options);

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
        var options = CreateValidOptions();
        options.Domains[domain] = new CacheOrchestratorOptions.DomainCacheSettings();

        var result = _sut.Validate(null, options);

        result.Succeeded.Should().BeFalse();
        result.Failures.Should().Contain(f =>
            f.Contains(domain, StringComparison.Ordinal) &&
            f.Contains("default", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_WhitespaceDomainKey_Fails()
    {
        var options = CreateValidOptions();
        options.Domains["   "] = new CacheOrchestratorOptions.DomainCacheSettings();

        var result = _sut.Validate(null, options);

        result.Succeeded.Should().BeFalse();
        result.Failures.Should().Contain(f => f.Contains("null or whitespace", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_DefaultDomainKey_ReturnsSuccess()
    {
        var options = CreateValidOptions();
        options.Domains["default"] = new CacheOrchestratorOptions.DomainCacheSettings();

        var result = _sut.Validate(null, options);

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
            OutputCache = { Provider = "InMemory" },
            FusionCacheInstances = new Dictionary<string, CacheOrchestratorOptions.FusionCacheInstanceOptions>(
                StringComparer.OrdinalIgnoreCase)
            {
                ["default"] = new CacheOrchestratorOptions.FusionCacheInstanceOptions { Provider = "InMemory" }
            }
        };
}
