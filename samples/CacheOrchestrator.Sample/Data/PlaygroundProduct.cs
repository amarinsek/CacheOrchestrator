namespace CacheOrchestrator.Sample.Data;

/// <summary>Row in the playground products table (Getting started + CRUD demos).</summary>
public sealed record PlaygroundProduct(
    string Id,
    string Name,
    decimal Price,
    DateTimeOffset UpdatedAt);
