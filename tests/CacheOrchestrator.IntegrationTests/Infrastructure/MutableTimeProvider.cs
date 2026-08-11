namespace CacheOrchestrator.IntegrationTests.Infrastructure;

/// <summary>
/// Test <see cref="TimeProvider"/> whose UTC clock can be advanced without waiting.
/// Registered as <c>services.AddSingleton&lt;TimeProvider&gt;(…)</c> before or after
/// <c>AddCacheOrchestrator</c> (which uses <c>TryAddSingleton(TimeProvider.System)</c>).
/// </summary>
public sealed class MutableTimeProvider : TimeProvider
{
    private DateTimeOffset _utcNow;

    public MutableTimeProvider(DateTimeOffset utcNow) => _utcNow = utcNow;

    public override DateTimeOffset GetUtcNow() => _utcNow;

    public void SetUtcNow(DateTimeOffset utcNow) => _utcNow = utcNow;

    public void Advance(TimeSpan delta) => _utcNow += delta;
}
