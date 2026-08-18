namespace CacheOrchestrator.AdminConsole.UnitTests;

/// <summary>Test <see cref="TimeProvider"/> whose UTC clock can be advanced without waiting.</summary>
internal sealed class TestMutableTimeProvider : TimeProvider
{
    private DateTimeOffset _utcNow;

    public TestMutableTimeProvider(DateTimeOffset utcNow) => _utcNow = utcNow;

    public override DateTimeOffset GetUtcNow() => _utcNow;

    public void Advance(TimeSpan delta) => _utcNow += delta;
}
