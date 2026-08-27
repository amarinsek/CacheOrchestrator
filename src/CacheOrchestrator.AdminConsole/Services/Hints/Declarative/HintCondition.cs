namespace CacheOrchestrator.AdminConsole.Services.Hints.Declarative;

public abstract class HintCondition
{
    public abstract bool Evaluate(HintEvaluationContext context);
}

public sealed class HintAllCondition(IReadOnlyList<HintCondition> children) : HintCondition
{
    public override bool Evaluate(HintEvaluationContext context) =>
        children.Count > 0 && children.All(c => c.Evaluate(context));
}

public sealed class HintAnyCondition(IReadOnlyList<HintCondition> children) : HintCondition
{
    public override bool Evaluate(HintEvaluationContext context) =>
        children.Count > 0 && children.Any(c => c.Evaluate(context));
}

public sealed class HintNotCondition(HintCondition inner) : HintCondition
{
    public override bool Evaluate(HintEvaluationContext context) => !inner.Evaluate(context);
}

public sealed class HintCompareCondition(string path, string op, JsonValue value) : HintCondition
{
    public override bool Evaluate(HintEvaluationContext context)
    {
        object? left = context.ResolvePath(path);
        return Compare(left, op, value);
    }

    private static bool Compare(object? left, string op, JsonValue right)
    {
        if (left is null)
            return op is "exists" ? false : op is "notexists" || op is "!exists";

        if (op is "exists")
            return true;
        if (op is "notexists" or "!exists")
            return false;

        if (op is "eq" or "==" or "=")
            return ValuesEqual(left, right);
        if (op is "ne" or "!=" or "<>")
            return !ValuesEqual(left, right);

        if (!TryToDouble(left, out double lNum) || !right.TryGetDouble(out double rNum))
        {
            // string compare for gt/lt not supported
            if (op is "contains" && left is string ls && right.TryGetString(out string? rs) && rs is not null)
                return ls.Contains(rs, StringComparison.OrdinalIgnoreCase);
            return false;
        }

        return op switch
        {
            "gt" or ">" => lNum > rNum,
            "gte" or ">=" => lNum >= rNum,
            "lt" or "<" => lNum < rNum,
            "lte" or "<=" => lNum <= rNum,
            _ => false
        };
    }

    private static bool ValuesEqual(object left, JsonValue right)
    {
        if (right.TryGetBool(out bool rb) && left is bool lb)
            return lb == rb;
        if (right.TryGetDouble(out double rd) && TryToDouble(left, out double ld))
            return Math.Abs(ld - rd) < 1e-12;
        if (right.TryGetString(out string? rs) && rs is not null)
            return string.Equals(Convert.ToString(left), rs, StringComparison.OrdinalIgnoreCase);
        return false;
    }

    private static bool TryToDouble(object value, out double number)
    {
        switch (value)
        {
            case double d:
                number = d;
                return true;
            case float f:
                number = f;
                return true;
            case int i:
                number = i;
                return true;
            case long l:
                number = l;
                return true;
            case decimal m:
                number = (double)m;
                return true;
            case bool b:
                number = b ? 1 : 0;
                return true;
            default:
                return double.TryParse(Convert.ToString(value), System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out number);
        }
    }
}

/// <summary>Lightweight JSON scalar for comparisons.</summary>
public readonly struct JsonValue
{
    private readonly object? _raw;

    private JsonValue(object? raw)
    {
        _raw = raw;
    }

    public static JsonValue From(System.Text.Json.JsonElement el) => el.ValueKind switch
    {
        System.Text.Json.JsonValueKind.String => new JsonValue(el.GetString()),
        System.Text.Json.JsonValueKind.Number => new JsonValue(el.GetDouble()),
        System.Text.Json.JsonValueKind.True => new JsonValue(true),
        System.Text.Json.JsonValueKind.False => new JsonValue(false),
        System.Text.Json.JsonValueKind.Null => new JsonValue(null),
        _ => new JsonValue(el.GetRawText())
    };

    public static JsonValue FromObject(object? o) => new(o);

    public bool TryGetDouble(out double d)
    {
        if (_raw is double x)
        {
            d = x;
            return true;
        }

        if (_raw is int i)
        {
            d = i;
            return true;
        }

        if (_raw is long l)
        {
            d = l;
            return true;
        }

        d = 0;
        return false;
    }

    public bool TryGetBool(out bool b)
    {
        if (_raw is bool x)
        {
            b = x;
            return true;
        }

        b = false;
        return false;
    }

    public bool TryGetString(out string? s)
    {
        if (_raw is string x)
        {
            s = x;
            return true;
        }

        s = null;
        return false;
    }
}
