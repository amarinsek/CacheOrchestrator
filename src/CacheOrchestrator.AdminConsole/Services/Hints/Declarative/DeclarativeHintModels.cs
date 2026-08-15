using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CacheOrchestrator.AdminConsole.Services.Hints.Declarative;

/// <summary>Root document for one or more rule files.</summary>
public sealed class DeclarativeHintDocument
{
    /// <summary>Optional pack name for diagnostics.</summary>
    public string? Name { get; set; }

    public List<DeclarativeHintRuleDefinition> Rules { get; set; } = [];
}

public sealed class DeclarativeHintRuleDefinition
{
    public string? Code { get; set; }
    public string? Severity { get; set; }
    public string? Category { get; set; }
    /// <summary><c>domain</c>, <c>endpoint</c>, or <c>any</c>.</summary>
    public string? Scope { get; set; }
    public string? Description { get; set; }
    public string? Message { get; set; }
    public bool Enabled { get; set; } = true;

    /// <summary>Condition tree: <c>all</c> / <c>any</c> / single compare, or a bare compare object.</summary>
    public JsonElement? When { get; set; }
}

internal static class DeclarativeHintJson
{
    /// <summary>Read/write options. Unsafe relaxed encoder so Settings UI shows <c>&gt;=</c> not <c>\u003E=</c>.</summary>
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false
    };

    public static readonly JsonSerializerOptions PrettyOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true
    };
}
