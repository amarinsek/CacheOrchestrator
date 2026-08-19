using CacheOrchestrator.Redis;
using StackExchange.Redis;

namespace CacheOrchestrator.Redis.UnitTests;

public class RedisCacheHealthProbeTests
{
    [Fact]
    public void Constructor_SetsName()
    {
        IConnectionMultiplexer mux = Substitute.For<IConnectionMultiplexer>();
        RedisCacheHealthProbe probe = new("redis:default", mux);
        probe.Name.Should().Be("redis:default");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Constructor_WhenNameIsNullOrEmpty_Throws(string? name)
    {
        IConnectionMultiplexer mux = Substitute.For<IConnectionMultiplexer>();
        var act = () => new RedisCacheHealthProbe(name!, mux);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_WhenMultiplexerIsNull_Throws()
    {
        var act = () => new RedisCacheHealthProbe("redis:default", null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task ProbeAsync_WhenNotConnected_Throws()
    {
        IConnectionMultiplexer mux = Substitute.For<IConnectionMultiplexer>();
        mux.IsConnected.Returns(false);
        RedisCacheHealthProbe probe = new("redis:default", mux);

        var act = () => probe.ProbeAsync(TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*redis:default*not connected*");
    }

    [Fact]
    public async Task ProbeAsync_WhenConnected_PingsDatabase()
    {
        IDatabase db = Substitute.For<IDatabase>();
        db.PingAsync(Arg.Any<CommandFlags>()).Returns(TimeSpan.FromMilliseconds(2));

        IConnectionMultiplexer mux = Substitute.For<IConnectionMultiplexer>();
        mux.IsConnected.Returns(true);
        mux.GetDatabase(Arg.Any<int>(), Arg.Any<object?>()).Returns(db);

        RedisCacheHealthProbe probe = new("redis:default", mux);
        await probe.ProbeAsync(TestContext.Current.CancellationToken);

        await db.Received(1).PingAsync(Arg.Any<CommandFlags>());
    }
}
