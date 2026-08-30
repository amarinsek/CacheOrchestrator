namespace CacheOrchestrator.Sample.Data;

/// <summary>Playground product database path (local file or shared lab volume).</summary>
public sealed class SampleSqliteOptions
{
    public const string SectionName = "Sample";

    /// <summary>
    /// SQLite file path. Relative paths resolve against the content root.
    /// Multi-instance labs 03–05 override this to <c>/shared/playground.db</c>.
    /// </summary>
    public string SqlitePath { get; set; } = "Data/playground.db";
}
