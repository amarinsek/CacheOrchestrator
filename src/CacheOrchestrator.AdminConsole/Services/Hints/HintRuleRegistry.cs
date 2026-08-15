using CacheOrchestrator.AdminConsole.Options;
using CacheOrchestrator.AdminConsole.Services.Hints.Declarative;
using Microsoft.Extensions.Options;

namespace CacheOrchestrator.AdminConsole.Services.Hints;

/// <summary>
/// Loads product <c>hints/core-hints.json</c> plus optional operator rule files.
/// </summary>
public sealed class HintRuleRegistry
{
    public const string CoreHintsRelativePath = "hints/core-hints.json";

    private readonly IOptionsMonitor<AdminConsoleOptions> _options;
    private readonly IHostEnvironment _env;
    private readonly ILogger<HintRuleRegistry> _logger;
    private readonly HintRuleCompiler _compiler = new();
    private readonly object _gate = new();

    private volatile IReadOnlyList<IHintRule> _rules = [];
    private volatile HintRuleLoadStatus _status = new()
    {
        Ok = true,
        RuleCount = 0,
        FileCount = 0,
        LoadedAtUtc = DateTimeOffset.UtcNow
    };

    public HintRuleRegistry(
        IOptionsMonitor<AdminConsoleOptions> options,
        IHostEnvironment env,
        ILogger<HintRuleRegistry> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(env);
        ArgumentNullException.ThrowIfNull(logger);
        _options = options;
        _env = env;
        _logger = logger;
        Reload();
    }

    public IReadOnlyList<IHintRule> GetRules() => _rules;

    public HintRuleLoadStatus GetLoadStatus() => _status;

    public HintRuleLoadStatus Reload()
    {
        lock (_gate)
        {
            List<IHintRule> rules = [];
            List<HintRuleCompileError> errors = [];
            HashSet<string> loadedFiles = new(StringComparer.OrdinalIgnoreCase);
            int files = 0;

            // 1) Product core pack (always)
            string coreFull = Path.GetFullPath(Path.Combine(_env.ContentRootPath, CoreHintsRelativePath));
            if (File.Exists(coreFull))
            {
                files++;
                loadedFiles.Add(coreFull);
                LoadOne(coreFull, CoreHintsRelativePath.Replace('\\', '/'), rules, errors);
            }
            else
            {
                errors.Add(new HintRuleCompileError
                {
                    File = CoreHintsRelativePath,
                    Path = "$",
                    Message = "Product core hints file is missing (hints/core-hints.json)."
                });
            }

            // 2) Operator packs from config (globs / paths); skip core if already loaded
            foreach (string pattern in _options.CurrentValue.Hints.RuleFiles)
            {
                if (string.IsNullOrWhiteSpace(pattern))
                    continue;

                foreach (string fullPath in ExpandPaths(pattern.Trim()))
                {
                    string full = Path.GetFullPath(fullPath);
                    if (!loadedFiles.Add(full))
                        continue;

                    files++;
                    string label = Path.GetRelativePath(_env.ContentRootPath, full);
                    if (label.StartsWith("..", StringComparison.Ordinal))
                        label = full;
                    LoadOne(full, label.Replace('\\', '/'), rules, errors);
                }
            }

            // Unique (code, scope) — same code may exist for domain + endpoint
            HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
            foreach (IHintRule r in rules)
            {
                string key = r.Code + "\0" + r.Scope;
                if (!seen.Add(key))
                {
                    errors.Add(new HintRuleCompileError
                    {
                        File = r.Source,
                        RuleCode = r.Code,
                        Path = "code",
                        Message = $"Duplicate rule code \"{r.Code}\" for scope \"{r.Scope}\"."
                    });
                }
            }

            _rules = rules;
            _status = new HintRuleLoadStatus
            {
                Ok = errors.Count == 0,
                RuleCount = rules.Count,
                FileCount = files,
                Errors = errors,
                LoadedAtUtc = DateTimeOffset.UtcNow
            };

            if (errors.Count > 0)
            {
                _logger.LogWarning(
                    "Hint rules loaded with {ErrorCount} issue(s); {RuleCount} rule(s) active from {FileCount} file(s).",
                    errors.Count, rules.Count, files);
            }
            else
            {
                _logger.LogInformation(
                    "Hint rules loaded: {RuleCount} rule(s) from {FileCount} file(s).",
                    rules.Count, files);
            }

            return _status;
        }
    }

    private void LoadOne(
        string fullPath,
        string label,
        List<IHintRule> rules,
        List<HintRuleCompileError> errors)
    {
        string json;
        try
        {
            json = File.ReadAllText(fullPath);
        }
        catch (Exception ex)
        {
            errors.Add(new HintRuleCompileError
            {
                File = label,
                Path = "$",
                Message = "Cannot read file: " + ex.Message
            });
            return;
        }

        HintRuleCompileBatchResult batch = _compiler.CompileFile(label, json);
        errors.AddRange(batch.Errors);
        foreach (IHintRule r in batch.Rules)
        {
            if (r is DeclarativeHintRule d && !d.DefinitionEnabled)
                continue;
            rules.Add(r);
        }
    }

    private IEnumerable<string> ExpandPaths(string pattern)
    {
        string combined = Path.Combine(_env.ContentRootPath, pattern);
        if (pattern.Contains('*') || pattern.Contains('?'))
        {
            string? dir = Path.GetDirectoryName(combined);
            string filePattern = Path.GetFileName(combined);
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
            {
                string relDir = Path.GetDirectoryName(pattern) ?? "";
                dir = Path.Combine(_env.ContentRootPath, relDir);
                filePattern = Path.GetFileName(pattern);
            }

            if (!Directory.Exists(dir))
                yield break;

            foreach (string f in Directory.GetFiles(dir, filePattern, SearchOption.TopDirectoryOnly))
            {
                if (f.EndsWith("disabled.local.json", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (f.EndsWith(".sample.json", StringComparison.OrdinalIgnoreCase))
                    continue;
                yield return f;
            }

            yield break;
        }

        string full = Path.GetFullPath(combined);
        if (File.Exists(full))
            yield return full;
    }
}
