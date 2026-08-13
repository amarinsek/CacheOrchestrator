namespace CacheOrchestrator.EFCore;

/// <summary>Resolved (domain, entityKind) for a CLR type.</summary>
internal readonly record struct EntityCacheMapping(string Domain, string EntityKind);
