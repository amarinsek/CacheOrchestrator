using CacheOrchestrator.Configuration;
using CacheOrchestrator.Diagnostics;
using CacheOrchestrator.Orchestration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CacheOrchestrator.Core.UnitTests.Diagnostics;

public class DataCacheProviderStartupDiagnosticTests
{
    [Fact]
    public async Task StartAsync_WhenDataCacheEnabledWithoutProvider_LogsWarning()
    {
        var logger = new RecordingLogger<DataCacheProviderStartupDiagnostic>();
        var sut = new DataCacheProviderStartupDiagnostic(
            NullDataCacheProvider.Instance,
            Options.Create(new CacheOrchestratorOptions()),
            logger);

        await sut.StartAsync(TestContext.Current.CancellationToken);

        logger.Levels.Should().ContainSingle().Which.Should().Be(LogLevel.Warning);
    }

    [Fact]
    public async Task StartAsync_WhenDataCacheExplicitlyDisabled_DoesNotWarn()
    {
        CacheOrchestratorOptions options = new()
        {
            DomainDefaults = new CacheOrchestratorOptions.DomainCacheSettings
            {
                DataCache = new DomainDataCacheSettings { Enabled = false }
            }
        };
        var logger = new RecordingLogger<DataCacheProviderStartupDiagnostic>();
        var sut = new DataCacheProviderStartupDiagnostic(
            NullDataCacheProvider.Instance,
            Options.Create(options),
            logger);

        await sut.StartAsync(TestContext.Current.CancellationToken);

        logger.Levels.Should().BeEmpty();
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<LogLevel> Levels { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) => Levels.Add(logLevel);
    }
}
