CacheOrchestrator 2.0.0

Breaking: entity identity is (domain, entityKind, resourceId). Use GetOrSetEntityAsync and InvalidateEntityAsync(domain, entityKind, id). Tags are entity:{domain}:{entityKind}:{id}.

New packages: CacheOrchestrator.Bus (HTTP command bus) and CacheOrchestrator.EFCore.Invalidation (purge after SaveChanges).

Also: Admin API (opt-in, MapCacheOrchestratorAdmin), Output Cache auth-bypass header fix, integration tests on net8 and net10, rewritten README and docs.

Full notes: https://github.com/amarinsek/CacheOrchestrator/blob/main/CHANGELOG.md
