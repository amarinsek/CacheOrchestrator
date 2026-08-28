using Microsoft.Extensions.Options;

namespace CacheOrchestrator.HttpBus.UnitTests;

public class HttpBusOptionsValidatorTests
{
    [Fact]
    public void Validate_WhenBusDisabled_AllowsMissingCredentials()
    {
        var sut = new HttpBusOptionsValidator();

        ValidateOptionsResult result = sut.Validate(null, new HttpBusOptions());

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validate_WhenAdminFallbackKeyPresent_Succeeds()
    {
        var options = new HttpBusOptions
        {
            Admin = { ApiKey = "admin-key" },
            Cluster = { Bus = { Enabled = true } }
        };

        ValidateOptionsResult result = new HttpBusOptionsValidator().Validate(null, options);

        result.Succeeded.Should().BeTrue();
    }

    [Theory]
    [InlineData(0, 30, "CommandMaxAgeSeconds")]
    [InlineData(86401, 30, "CommandMaxAgeSeconds")]
    [InlineData(300, -1, "ClockSkewSeconds")]
    [InlineData(300, 3601, "ClockSkewSeconds")]
    public void Validate_WhenFreshnessWindowInvalid_Fails(
        int maxAgeSeconds,
        int clockSkewSeconds,
        string expectedSetting)
    {
        var options = new HttpBusOptions
        {
            Cluster =
            {
                Bus =
                {
                    Enabled = true,
                    ApiKey = "key",
                    CommandMaxAgeSeconds = maxAgeSeconds,
                    ClockSkewSeconds = clockSkewSeconds
                }
            }
        };

        ValidateOptionsResult result = new HttpBusOptionsValidator().Validate(null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain(expectedSetting);
    }

    [Fact]
    public void Validate_WhenDedupeWindowDoesNotCoverTimestampValidity_Fails()
    {
        var options = new HttpBusOptions
        {
            Cluster =
            {
                Bus =
                {
                    Enabled = true,
                    ApiKey = "key",
                    DedupeWindowSeconds = 329,
                    CommandMaxAgeSeconds = 300,
                    ClockSkewSeconds = 30
                }
            }
        };

        ValidateOptionsResult result = new HttpBusOptionsValidator().Validate(null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("DedupeWindowSeconds");
    }
}
