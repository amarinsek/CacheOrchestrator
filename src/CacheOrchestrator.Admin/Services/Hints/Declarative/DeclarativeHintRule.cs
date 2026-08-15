using System.Globalization;
using System.Text.RegularExpressions;
using CacheOrchestrator.Admin;

namespace CacheOrchestrator.Admin.App.Services.Hints.Declarative;

/// <summary>One compiled declarative rule (JSON).</summary>
public sealed partial class DeclarativeHintRule : IHintRule
{
    private readonly HintCondition _when;
    private readonly string _messageTemplate;
    private readonly string _severity;
    private readonly bool _definitionEnabled;

    public DeclarativeHintRule(
        string id,
        string code,
        string? category,
        string scope,
        string source,
        string? description,
        string severity,
        string messageTemplate,
        HintCondition when,
        bool definitionEnabled,
        string? definitionJson = null)
    {
        Id = id;
        Code = code;
        Category = category;
        Scope = scope;
        Source = source;
        Description = description;
        DefaultSeverity = severity;
        _severity = severity;
        _messageTemplate = messageTemplate;
        _when = when;
        _definitionEnabled = definitionEnabled;
        DefinitionJson = definitionJson;
        EmittedCodes = [code];
    }

    public string Id { get; }
    public string Code { get; }
    public string? Category { get; }
    public string Scope { get; }
    public string Source { get; }
    public string? Description { get; }
    public string? DefaultSeverity { get; }
    public IReadOnlyList<string> EmittedCodes { get; }

    /// <summary>Pretty JSON of the original rule object (for Settings detail view).</summary>
    public string? DefinitionJson { get; }

    /// <summary>False when the rule file marks <c>enabled: false</c>.</summary>
    public bool DefinitionEnabled => _definitionEnabled;

    public IEnumerable<AdminHintDto> Evaluate(HintEvaluationContext context)
    {
        if (!_definitionEnabled)
            yield break;

        string scope = Scope.ToLowerInvariant();
        if (scope is "domain" && context.Domain is null)
            yield break;
        if (scope is "endpoint" && context.Endpoint is null)
            yield break;

        if (!_when.Evaluate(context))
            yield break;

        yield return new AdminHintDto
        {
            Severity = _severity,
            Code = Code,
            Message = FormatMessage(_messageTemplate, context)
        };
    }

    private static string FormatMessage(string template, HintEvaluationContext context)
    {
        return PlaceholderRegex().Replace(template, m =>
        {
            string expr = m.Groups[1].Value.Trim();
            string path = expr;
            string? format = null;
            int colon = expr.IndexOf(':');
            if (colon > 0)
            {
                path = expr[..colon].Trim();
                format = expr[(colon + 1)..].Trim();
            }

            object? val = context.ResolvePath(path);
            if (val is null)
                return "—";

            if (format is "p0" or "p1" or "p2" && TryDouble(val, out double pct))
            {
                int digs = format[1] - '0';
                return (pct * 100).ToString("0." + new string('#', digs), CultureInfo.InvariantCulture) + "%";
            }

            if (format is "0" or "0.#" or "0.0" or "0.00" && TryDouble(val, out double num))
                return num.ToString(format, CultureInfo.InvariantCulture);

            return Convert.ToString(val, CultureInfo.InvariantCulture) ?? "—";
        });
    }

    private static bool TryDouble(object val, out double d)
    {
        switch (val)
        {
            case double x:
                d = x;
                return true;
            case float f:
                d = f;
                return true;
            case int i:
                d = i;
                return true;
            case long l:
                d = l;
                return true;
            default:
                return double.TryParse(Convert.ToString(val, CultureInfo.InvariantCulture),
                    NumberStyles.Any, CultureInfo.InvariantCulture, out d);
        }
    }

    [GeneratedRegex(@"\{([^{}]+)\}", RegexOptions.Compiled)]
    private static partial Regex PlaceholderRegex();
}
