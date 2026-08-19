using CacheOrchestrator.Configuration;

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
            FusionCacheInstance = "pii"
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
            FusionCacheInstance = "nonexistent"
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
            FusionCacheInstance = null
        };

        var result = _sut.Validate(null, options);

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validate_NegativeDomainDefaults_OutputCacheTtl_Fails()
    {
        var options = CreateValidOptions();
        options.DomainDefaults.OutputCacheTtlSeconds = -1;

        var result = _sut.Validate(null, options);

        result.Succeeded.Should().BeFalse();
        result.Failures.Should().Contain(f => f.Contains("OutputCacheTtlSeconds", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_NegativeDomainDefaults_FusionCacheSoftTtl_Fails()
    {
        var options = CreateValidOptions();
        options.DomainDefaults.FusionCacheSoftTtlSeconds = -5;

        var result = _sut.Validate(null, options);

        result.Succeeded.Should().BeFalse();
        result.Failures.Should().Contain(f => f.Contains("FusionCacheSoftTtlSeconds", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_NegativeDomainDefaults_FusionCacheHardTtl_Fails()
    {
        var options = CreateValidOptions();
        options.DomainDefaults.FusionCacheHardTtlSeconds = -10;

        var result = _sut.Validate(null, options);

        result.Succeeded.Should().BeFalse();
        result.Failures.Should().Contain(f => f.Contains("FusionCacheHardTtlSeconds", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_NegativeDomainSpecific_OutputCacheTtl_Fails()
    {
        var options = CreateValidOptions();
        options.Domains["products"] = new CacheOrchestratorOptions.DomainCacheSettings
        {
            OutputCacheTtlSeconds = -3
        };

        var result = _sut.Validate(null, options);

        result.Succeeded.Should().BeFalse();
        result.Failures.Should().Contain(f =>
            f.Contains("products", StringComparison.OrdinalIgnoreCase) &&
            f.Contains("OutputCacheTtlSeconds", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_ZeroTtls_AreAllowed()
    {
        var options = CreateValidOptions();
        options.DomainDefaults.OutputCacheTtlSeconds = 0;
        options.DomainDefaults.FusionCacheSoftTtlSeconds = 0;
        options.DomainDefaults.FusionCacheHardTtlSeconds = 0;
        options.DomainDefaults.ClientTtlSeconds = 0;
        options.DomainDefaults.FusionCacheEagerRefreshRatio = 0;

        var result = _sut.Validate(null, options);

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validate_NegativeClientTtl_Fails()
    {
        var options = CreateValidOptions();
        options.DomainDefaults.ClientTtlSeconds = -1;

        var result = _sut.Validate(null, options);

        result.Succeeded.Should().BeFalse();
        result.Failures.Should().Contain(f => f.Contains("ClientTtlSeconds", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_NegativeFailSafe_Fails()
    {
        var options = CreateValidOptions();
        options.DomainDefaults.FusionCacheFailSafeSeconds = -1;

        var result = _sut.Validate(null, options);

        result.Succeeded.Should().BeFalse();
        result.Failures.Should().Contain(f => f.Contains("FusionCacheFailSafeSeconds", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_EagerRefreshRatio_One_Fails()
    {
        var options = CreateValidOptions();
        options.DomainDefaults.FusionCacheEagerRefreshRatio = 1.0;

        var result = _sut.Validate(null, options);

        result.Succeeded.Should().BeFalse();
        result.Failures.Should().Contain(f => f.Contains("FusionCacheEagerRefreshRatio", StringComparison.OrdinalIgnoreCase));
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
