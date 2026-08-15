using System.Collections.Concurrent;
using System.Text.Json;
using CacheOrchestrator.AdminConsole.Options;
using Microsoft.Extensions.Options;

namespace CacheOrchestrator.AdminConsole.Services.Hints;

/// <summary>
/// Merges <c>AdminConsole:Hints:DisabledCodes</c> with a local JSON override file
/// written by the Settings UI.
/// </summary>
public sealed class HintRuleDisableStore : IHintRuleDisableStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly IOptionsMonitor<AdminConsoleOptions> _options;
    private readonly IHostEnvironment _env;
    private readonly ConcurrentDictionary<string, byte> _runtimeDisabled =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly object _fileLock = new();
    private bool _loaded;

    public HintRuleDisableStore(IOptionsMonitor<AdminConsoleOptions> options, IHostEnvironment env)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(env);
        _options = options;
        _env = env;
        EnsureLoaded();
    }

    public bool IsDisabled(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return false;
        EnsureLoaded();
        if (_runtimeDisabled.ContainsKey(code))
            return true;
        foreach (string c in _options.CurrentValue.Hints.DisabledCodes)
        {
            if (string.Equals(c, code, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    public IReadOnlyCollection<string> GetDisabledCodes()
    {
        EnsureLoaded();
        HashSet<string> set = new(StringComparer.OrdinalIgnoreCase);
        foreach (string c in _options.CurrentValue.Hints.DisabledCodes)
        {
            if (!string.IsNullOrWhiteSpace(c))
                set.Add(c.Trim());
        }

        foreach (string c in _runtimeDisabled.Keys)
            set.Add(c);
        return set;
    }

    public Task SetEnabledAsync(string code, bool enabled, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        EnsureLoaded();
        string key = code.Trim();
        if (enabled)
            _runtimeDisabled.TryRemove(key, out _);
        else
            _runtimeDisabled[key] = 0;

        Persist();
        return Task.CompletedTask;
    }

    private void EnsureLoaded()
    {
        if (_loaded)
            return;
        lock (_fileLock)
        {
            if (_loaded)
                return;
            string path = ResolvePath();
            if (File.Exists(path))
            {
                try
                {
                    string json = File.ReadAllText(path);
                    DisabledFile? doc = JsonSerializer.Deserialize<DisabledFile>(json, JsonOptions);
                    if (doc?.DisabledCodes is { Count: > 0 })
                    {
                        foreach (string c in doc.DisabledCodes)
                        {
                            if (!string.IsNullOrWhiteSpace(c))
                                _runtimeDisabled[c.Trim()] = 0;
                        }
                    }
                }
                catch
                {
                    // Corrupt file: start empty; next save rewrites.
                }
            }

            _loaded = true;
        }
    }

    private void Persist()
    {
        lock (_fileLock)
        {
            string path = ResolvePath();
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var doc = new DisabledFile
            {
                DisabledCodes = _runtimeDisabled.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList()
            };
            File.WriteAllText(path, JsonSerializer.Serialize(doc, JsonOptions));
        }
    }

    private string ResolvePath()
    {
        string rel = _options.CurrentValue.Hints.DisabledStatePath;
        if (string.IsNullOrWhiteSpace(rel))
            rel = "hints/disabled.local.json";
        return Path.GetFullPath(Path.Combine(_env.ContentRootPath, rel));
    }

    private sealed class DisabledFile
    {
        public List<string> DisabledCodes { get; set; } = [];
    }
}
