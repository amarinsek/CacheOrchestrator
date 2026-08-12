CacheOrchestrator 2.0.0 — breaking: entity identity requires entityKind.

A domain is a cache policy group. Row identity is (domain, entityKind, resourceId).

- GetOrSetEntityAsync + InvalidateEntityAsync(domain, entityKind, id)
- Tags: entity:{domain}:{entityKind}:{id} and entitykind:{domain}:{entityKind}
- CacheOutputWithDomain(..., resourceRouteKey, entityKind)

Migrate call sites; old entity entries expire by TTL or InvalidateDomainAsync / Version bump.

Full notes: https://github.com/amarinsek/CacheOrchestrator/blob/main/CHANGELOG.md
