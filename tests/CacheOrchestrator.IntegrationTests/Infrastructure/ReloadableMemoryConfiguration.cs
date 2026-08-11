using Microsoft.Extensions.Configuration;

namespace CacheOrchestrator.IntegrationTests.Infrastructure;

/// <summary>
/// In-memory <see cref="IConfiguration"/> source that can mutate values and raise a reload token
/// (same path as file <c>reloadOnChange</c> for <see cref="Microsoft.Extensions.Options.IOptionsMonitor{TOptions}"/>).
/// </summary>
public sealed class ReloadableMemoryConfigurationSource : IConfigurationSource
{
    private readonly Dictionary<string, string?> _data;

    public ReloadableMemoryConfigurationSource(IEnumerable<KeyValuePair<string, string?>> initial)
    {
        _data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, string?> pair in initial)
            _data[pair.Key] = pair.Value;
    }

    /// <summary>Provider after <see cref="Build"/>; null until the configuration root is built.</summary>
    public ReloadableMemoryConfigurationProvider? Provider { get; private set; }

    /// <inheritdoc />
    public IConfigurationProvider Build(IConfigurationBuilder builder)
    {
        Provider = new ReloadableMemoryConfigurationProvider(_data);
        return Provider;
    }
}

/// <summary>
/// Mutable memory configuration provider that calls <see cref="ConfigurationProvider.OnReload"/> after updates.
/// </summary>
public sealed class ReloadableMemoryConfigurationProvider : ConfigurationProvider
{
    public ReloadableMemoryConfigurationProvider(IDictionary<string, string?> initial)
    {
        Data = new Dictionary<string, string?>(initial, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Sets a key and notifies configuration listeners (options reload).</summary>
    public void SetAndReload(string key, string? value)
    {
        Data[key] = value;
        OnReload();
    }
}
