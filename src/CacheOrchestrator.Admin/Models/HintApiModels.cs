namespace CacheOrchestrator.Admin.App.Models;

/// <summary>Body for <c>PUT /api/hints/rules/{code}/enabled</c>.</summary>
public sealed class HintRuleEnableRequest
{
    public bool Enabled { get; set; } = true;
}
