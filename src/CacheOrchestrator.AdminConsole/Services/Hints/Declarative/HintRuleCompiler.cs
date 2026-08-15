using System.Text.Json;
using System.Text.RegularExpressions;

namespace CacheOrchestrator.AdminConsole.Services.Hints.Declarative;

/// <summary>
/// Parses and validates declarative hint rule documents. Reports path-scoped errors for operators.
/// </summary>
public sealed partial class HintRuleCompiler
{
    private static readonly HashSet<string> Severities = new(StringComparer.OrdinalIgnoreCase)
    {
        "Info", "Warning", "Critical"
    };

    private static readonly HashSet<string> Scopes = new(StringComparer.OrdinalIgnoreCase)
    {
        "domain", "endpoint", "any"
    };

    private static readonly HashSet<string> Ops = new(StringComparer.OrdinalIgnoreCase)
    {
        "eq", "==", "=", "ne", "!=", "<>",
        "gt", ">", "gte", ">=", "lt", "<", "lte", "<=",
        "exists", "notexists", "!exists", "contains"
    };

    public HintRuleCompileBatchResult CompileFile(string fileLabel, string json)
    {
        List<HintRuleCompileError> errors = [];
        List<IHintRule> rules = [];

        DeclarativeHintDocument? doc;
        try
        {
            doc = JsonSerializer.Deserialize<DeclarativeHintDocument>(json, DeclarativeHintJson.Options);
        }
        catch (JsonException ex)
        {
            errors.Add(Err(fileLabel, "$", "Invalid JSON: " + ex.Message));
            return new HintRuleCompileBatchResult(false, rules, errors);
        }

        if (doc is null)
        {
            errors.Add(Err(fileLabel, "$", "Document is empty."));
            return new HintRuleCompileBatchResult(false, rules, errors);
        }

        if (doc.Rules is null || doc.Rules.Count == 0)
        {
            errors.Add(Err(fileLabel, "rules", "At least one rule is required (property \"rules\")."));
            return new HintRuleCompileBatchResult(false, rules, errors);
        }

        for (int i = 0; i < doc.Rules.Count; i++)
        {
            string basePath = $"rules[{i}]";
            DeclarativeHintRuleDefinition def = doc.Rules[i];
            string? codeHint = string.IsNullOrWhiteSpace(def.Code) ? null : def.Code.Trim();
            if (!TryCompileRule(fileLabel, basePath, def, out DeclarativeHintRule? rule, out List<HintRuleCompileError> ruleErrors))
            {
                // Ensure every error carries the rule code when we know it.
                foreach (HintRuleCompileError e in ruleErrors)
                {
                    errors.Add(e.RuleCode is null && codeHint is not null
                        ? e with { RuleCode = codeHint }
                        : e);
                }

                continue;
            }

            rules.Add(rule!);
        }

        return new HintRuleCompileBatchResult(errors.Count == 0, rules, errors);
    }

    private static bool TryCompileRule(
        string file,
        string basePath,
        DeclarativeHintRuleDefinition def,
        out DeclarativeHintRule? rule,
        out List<HintRuleCompileError> errors)
    {
        errors = [];
        rule = null;

        string? codeHint = string.IsNullOrWhiteSpace(def.Code) ? null : def.Code.Trim();

        if (codeHint is null)
        {
            errors.Add(Err(file, basePath + ".code", "\"code\" is required (stable machine id)."));
            return false;
        }

        string code = codeHint;
        if (code.Length > 80 || code.Any(c => !(char.IsLetterOrDigit(c) || c is '-' or '_' or '.')))
        {
            errors.Add(Err(file, basePath + ".code",
                "\"code\" must be 1–80 chars: letters, digits, '-', '_', '.'.", code));
        }

        string severity = string.IsNullOrWhiteSpace(def.Severity) ? "Info" : def.Severity.Trim();
        if (!Severities.Contains(severity))
        {
            errors.Add(Err(file, basePath + ".severity",
                "\"severity\" must be Info, Warning, or Critical.", code));
        }
        else
        {
            severity = Severities.First(s => s.Equals(severity, StringComparison.OrdinalIgnoreCase));
        }

        string scope = string.IsNullOrWhiteSpace(def.Scope) ? "domain" : def.Scope.Trim().ToLowerInvariant();
        if (!Scopes.Contains(scope))
        {
            errors.Add(Err(file, basePath + ".scope",
                "\"scope\" must be domain, endpoint, or any.", code));
        }

        if (string.IsNullOrWhiteSpace(def.Message))
        {
            errors.Add(Err(file, basePath + ".message", "\"message\" is required.", code));
        }

        if (def.When is null || def.When.Value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            errors.Add(Err(file, basePath + ".when", "\"when\" condition is required.", code));
            return false;
        }

        HintCondition? when = ParseCondition(file, basePath + ".when", def.When.Value, errors, code);
        if (errors.Count > 0 || when is null)
            return false;

        string? definitionJson = null;
        try
        {
            // Pretty + unescaped <>& so Settings modal shows ">=", not "\u003E="
            definitionJson = JsonSerializer.Serialize(def, DeclarativeHintJson.PrettyOptions);
        }
        catch
        {
            // non-fatal
        }

        rule = new DeclarativeHintRule(
            id: code,
            code: code,
            category: def.Category?.Trim(),
            scope: scope,
            source: "file:" + file,
            description: def.Description?.Trim() ?? def.Message?.Trim(),
            severity: severity,
            messageTemplate: def.Message!.Trim(),
            when: when,
            definitionEnabled: def.Enabled,
            definitionJson: definitionJson);

        return true;
    }

    private static HintCondition? ParseCondition(
        string file,
        string path,
        JsonElement el,
        List<HintRuleCompileError> errors,
        string? ruleCode)
    {
        if (el.ValueKind != JsonValueKind.Object)
        {
            errors.Add(Err(file, path, "Condition must be a JSON object (all/any/not/compare).", ruleCode));
            return null;
        }

        if (el.TryGetProperty("all", out JsonElement allEl))
        {
            if (allEl.ValueKind != JsonValueKind.Array || allEl.GetArrayLength() == 0)
            {
                errors.Add(Err(file, path + ".all", "\"all\" must be a non-empty array of conditions.", ruleCode));
                return null;
            }

            List<HintCondition> kids = [];
            int i = 0;
            foreach (JsonElement child in allEl.EnumerateArray())
            {
                HintCondition? c = ParseCondition(file, $"{path}.all[{i}]", child, errors, ruleCode);
                if (c is not null)
                    kids.Add(c);
                i++;
            }

            return kids.Count == allEl.GetArrayLength() ? new HintAllCondition(kids) : null;
        }

        if (el.TryGetProperty("any", out JsonElement anyEl))
        {
            if (anyEl.ValueKind != JsonValueKind.Array || anyEl.GetArrayLength() == 0)
            {
                errors.Add(Err(file, path + ".any", "\"any\" must be a non-empty array of conditions.", ruleCode));
                return null;
            }

            List<HintCondition> kids = [];
            int i = 0;
            foreach (JsonElement child in anyEl.EnumerateArray())
            {
                HintCondition? c = ParseCondition(file, $"{path}.any[{i}]", child, errors, ruleCode);
                if (c is not null)
                    kids.Add(c);
                i++;
            }

            return kids.Count == anyEl.GetArrayLength() ? new HintAnyCondition(kids) : null;
        }

        if (el.TryGetProperty("not", out JsonElement notEl))
        {
            HintCondition? inner = ParseCondition(file, path + ".not", notEl, errors, ruleCode);
            return inner is null ? null : new HintNotCondition(inner);
        }

        if (!el.TryGetProperty("path", out JsonElement pathEl) || pathEl.ValueKind != JsonValueKind.String)
        {
            errors.Add(Err(file, path + ".path", "Compare condition requires string \"path\".", ruleCode));
            return null;
        }

        string fieldPath = pathEl.GetString()!.Trim();
        if (!HintPathCatalog.IsKnown(fieldPath))
        {
            errors.Add(Err(file, path + ".path",
                $"Unknown path \"{fieldPath}\". Use a documented field (e.g. domain.fc.originShare).",
                ruleCode));
        }

        if (!el.TryGetProperty("op", out JsonElement opEl) || opEl.ValueKind != JsonValueKind.String)
        {
            errors.Add(Err(file, path + ".op", "Compare condition requires string \"op\".", ruleCode));
            return null;
        }

        string op = opEl.GetString()!.Trim();
        if (!Ops.Contains(op))
        {
            errors.Add(Err(file, path + ".op",
                $"Unknown op \"{op}\". Use eq, ne, gt, gte, lt, lte, exists, notexists, contains.",
                ruleCode));
        }

        bool needsValue = op is not ("exists" or "notexists" or "!exists");
        JsonValue value = JsonValue.FromObject(null);
        if (needsValue)
        {
            if (!el.TryGetProperty("value", out JsonElement valEl))
            {
                errors.Add(Err(file, path + ".value", $"Op \"{op}\" requires \"value\".", ruleCode));
                return null;
            }

            value = JsonValue.From(valEl);
        }

        // Only abort this node when *this* call added hard failures for required fields.
        // Unknown path / op already recorded; still fail the rule via errors.Count at parent.
        if (!Ops.Contains(op) || !HintPathCatalog.IsKnown(fieldPath))
            return null;

        return new HintCompareCondition(fieldPath, op, value);
    }

    private static HintRuleCompileError Err(string file, string path, string message, string? ruleCode = null) =>
        new()
        {
            File = file,
            Path = ToRuleRelativePath(path, ruleCode),
            Message = message,
            RuleCode = ruleCode
        };

    /// <summary>
    /// When the rule code is known, drop the <c>rules[n].</c> prefix so operators see
    /// <c>when.all[0].op</c> instead of <c>rules[12].when.all[0].op</c>.
    /// </summary>
    private static string ToRuleRelativePath(string path, string? ruleCode)
    {
        if (string.IsNullOrEmpty(ruleCode) || string.IsNullOrEmpty(path))
            return path;

        Match m = RulesIndexPathRegex().Match(path);
        return m.Success ? m.Groups[1].Value : path;
    }

    [GeneratedRegex(@"^rules\[\d+\]\.(.+)$", RegexOptions.CultureInvariant)]
    private static partial Regex RulesIndexPathRegex();
}

/// <summary>Result of compiling one file.</summary>
public sealed class HintRuleCompileBatchResult
{
    public HintRuleCompileBatchResult(
        bool success,
        IReadOnlyList<IHintRule> rules,
        IReadOnlyList<HintRuleCompileError> errors)
    {
        Success = success;
        Rules = rules;
        Errors = errors;
    }

    public bool Success { get; }
    public IReadOnlyList<IHintRule> Rules { get; }
    public IReadOnlyList<HintRuleCompileError> Errors { get; }
}
