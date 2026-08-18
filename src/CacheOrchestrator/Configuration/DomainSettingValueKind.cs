namespace CacheOrchestrator.Configuration;

/// <summary>UI / validation kind for a <see cref="DomainSettingAttribute"/>.</summary>
public enum DomainSettingValueKind
{
    /// <summary>Non-negative integer seconds.</summary>
    IntSeconds = 0,

    /// <summary>Boolean flag.</summary>
    Bool = 1,

    /// <summary>Free string (e.g. Fusion instance name).</summary>
    String = 2,

    /// <summary>UTC instant.</summary>
    DateTimeOffset = 3,

    /// <summary>Named enum (values listed in the catalog).</summary>
    Enum = 4,

    /// <summary>Floating ratio or similar.</summary>
    Double = 5,

    /// <summary>Integer (bytes, counts) — not necessarily seconds.</summary>
    Int = 6,

    /// <summary>Integer array (phase 2 overlay).</summary>
    IntArray = 7,

    /// <summary>String array (phase 2 overlay).</summary>
    StringArray = 8,
}
