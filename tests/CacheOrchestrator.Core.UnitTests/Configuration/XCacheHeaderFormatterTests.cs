using CacheOrchestrator.Configuration;

namespace CacheOrchestrator.Core.UnitTests.Configuration;

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

        header.Should().Be("domain=catalog; version=v1; client=public; phase=calm; oc=miss; fc=hit; ms=12");
        header.Should().NotContain("fa=");
    }

    [Fact]
    public void Format_Hit_OmitsFcMsAndFa_StillIncludesPhase()
    {
        string header = XCacheHeaderFormatter.Format(
            "products",
            ClientCacheClass.Private,
            OutputCacheResult.Hit,
            DataCacheResult.Miss,
            99,
            "v2",
            ClientCacheSchedulePhase.Approaching);

        header.Should().Be("domain=products; version=v2; client=private; phase=approaching; oc=hit");
        header.Should().NotContain("fc=");
        header.Should().NotContain("fa=");
        header.Should().NotContain("ms=");
    }

    [Theory]
    [InlineData(ClientCacheSchedulePhase.Calm, "calm")]
    [InlineData(ClientCacheSchedulePhase.Approaching, "approaching")]
    [InlineData(ClientCacheSchedulePhase.Hold, "hold")]
    [InlineData(ClientCacheSchedulePhase.NotApplicable, "n/a")]
    public void PhaseToString_MapsAllPhases(ClientCacheSchedulePhase phase, string expected) =>
        XCacheHeaderFormatter.PhaseToString(phase).Should().Be(expected);

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
        header.Should().Contain("oc=bypass");
        header.Should().NotContain("fc=");
        header.Should().NotContain("fa=");
    }

    [Fact]
    public void Format_OutputOff_WritesOffToken()
    {
        string header = XCacheHeaderFormatter.Format(
            "catalog",
            ClientCacheClass.Public,
            OutputCacheResult.Off,
            DataCacheResult.Off,
            8,
            "1");

        header.Should().Contain("oc=off");
        header.Should().Contain("fc=off");
        header.Should().Contain("fa=run");
        header.Should().Contain("ms=8");
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

        header.Should().Contain("fc=unresolved");
        header.Should().Contain("fa=run");
    }

    [Theory]
    [InlineData(DataCacheResult.Hit, false)]
    [InlineData(DataCacheResult.Miss, true)]
    [InlineData(DataCacheResult.Stale, true)]
    [InlineData(DataCacheResult.Bypass, true)]
    [InlineData(DataCacheResult.Off, true)]
    [InlineData(DataCacheResult.Unresolved, true)]
    public void Format_FaRun_WhenFcPresentAndNotHit(DataCacheResult data, bool expectFa)
    {
        string header = XCacheHeaderFormatter.Format(
            "catalog",
            ClientCacheClass.Public,
            OutputCacheResult.Miss,
            data,
            4,
            "1");

        header.Should().Contain("oc=miss");
        header.Should().Contain($"fc={XCacheDataToken(data)}");
        if (expectFa)
            header.Should().Contain("fa=run");
        else
            header.Should().NotContain("fa=");
    }

    private static string XCacheDataToken(DataCacheResult data) => data switch
    {
        DataCacheResult.Hit => "hit",
        DataCacheResult.Miss => "miss",
        DataCacheResult.Stale => "stale",
        DataCacheResult.Bypass => "bypass",
        DataCacheResult.Off => "off",
        DataCacheResult.Unresolved => "unresolved",
        _ => throw new ArgumentOutOfRangeException(nameof(data), data, null)
    };
}
