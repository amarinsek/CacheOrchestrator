using CacheOrchestrator.Bus;
using System.Net;

namespace CacheOrchestrator.Bus.UnitTests;

public class ServiceDiscoveryMembershipTests
{
    [Fact]
    public void TryCreateBaseUrl_FromDnsEndPoint()
    {
        bool ok = ServiceDiscoveryClusterMembership.TryCreateBaseUrl(
            new DnsEndPoint("10.0.0.5", 8080),
            "http",
            out Uri? uri);

        ok.Should().BeTrue();
        uri!.ToString().Should().Be("http://10.0.0.5:8080/");
    }

    [Fact]
    public void TryCreateBaseUrl_FromIPEndPoint()
    {
        bool ok = ServiceDiscoveryClusterMembership.TryCreateBaseUrl(
            new IPEndPoint(IPAddress.Parse("127.0.0.1"), 5001),
            "https",
            out Uri? uri);

        ok.Should().BeTrue();
        uri!.ToString().Should().Be("https://127.0.0.1:5001/");
    }

    [Theory]
    [InlineData("app1", "http", "http://app1")]
    [InlineData("http://app1", "http", "http://app1")]
    [InlineData("https+http://app1", "http", "https+http://app1")]
    public void NormalizeServiceQuery_AddsSchemeWhenMissing(string input, string scheme, string expected)
    {
        ServiceDiscoveryClusterMembership.NormalizeServiceQuery(input, scheme).Should().Be(expected);
    }

    [Fact]
    public void TryCreateBaseUrl_FromIPv6EndPoint()
    {
        bool ok = ServiceDiscoveryClusterMembership.TryCreateBaseUrl(
            new IPEndPoint(IPAddress.Parse("::1"), 8080),
            "http",
            out Uri? uri);

        ok.Should().BeTrue();
        uri!.ToString().Should().Be("http://[::1]:8080/");
    }

    [Fact]
    public void TryCreateBaseUrl_WhenEndPointIsNull_ReturnsFalse()
    {
        ServiceDiscoveryClusterMembership.TryCreateBaseUrl(null!, "http", out Uri? uri).Should().BeFalse();
        uri.Should().BeNull();
    }
}
