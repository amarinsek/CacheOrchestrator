using CacheOrchestrator.Configuration;

namespace CacheOrchestrator.UnitTests.Configuration;

public class XCacheHeaderFormatterTests
{
    [Fact]
    public void Format_IncludesPhase_AfterClient()
    {
        string header = XCacheHeaderFormatter.Format(
            "catalog",
            ClientCacheClass.Public,
            OutputCacheResult.Miss,
            DataCacheResult.Hit,
            12,
            "v1",
            ClientCacheSchedulePhase.Calm);

        header.Should().Be("domain=catalog; version=v1; client=public; phase=calm; output=miss; data=hit; ms=12");
    }

    [Fact]
    public void Format_Hit_OmitsDataAndMs_StillIncludesPhase()
    {
        string header = XCacheHeaderFormatter.Format(
            "products",
            ClientCacheClass.Private,
            OutputCacheResult.Hit,
            DataCacheResult.Miss,
            99,
            "v2",
            ClientCacheSchedulePhase.Approaching);

        header.Should().Be("domain=products; version=v2; client=private; phase=approaching; output=hit");
        header.Should().NotContain("data=");
        header.Should().NotContain("ms=");
    }

    [Theory]
    [InlineData(ClientCacheSchedulePhase.Calm, "calm")]
    [InlineData(ClientCacheSchedulePhase.Approaching, "approaching")]
    [InlineData(ClientCacheSchedulePhase.Hold, "hold")]
    [InlineData(ClientCacheSchedulePhase.NotApplicable, "n/a")]
    public void PhaseToString_MapsAllPhases(ClientCacheSchedulePhase phase, string expected) => XCacheHeaderFormatter.PhaseToString(phase).Should().Be(expected);

    [Fact]
    public void Format_DefaultPhase_IsNotApplicable()
    {
        string header = XCacheHeaderFormatter.Format(
            "x",
            ClientCacheClass.Public,
            OutputCacheResult.Bypass,
            null,
            null,
            "1");

        header.Should().Contain("phase=n/a");
        header.Should().Contain("version=1");
    }

    [Fact]
    public void Format_UnresolvedData_ShowsUnresolvedToken()
    {
        string header = XCacheHeaderFormatter.Format(
            "default",
            ClientCacheClass.Public,
            OutputCacheResult.Miss,
            DataCacheResult.Unresolved,
            null,
            "1",
            ClientCacheSchedulePhase.NotApplicable);

        header.Should().Contain("data=unresolved");
    }
}