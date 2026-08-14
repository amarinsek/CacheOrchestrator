# Local Prometheus (dev)

Scrapes the [Playground sample](../../samples/CacheOrchestrator.Sample) metrics endpoint and feeds the Admin App **Metrics** page.

The [Minimal sample](../../samples/CacheOrchestrator.Minimal) stays package-free and does **not** export `/metrics`.

## Prerequisites

- [Docker Desktop](https://www.docker.com/products/docker-desktop/) (or Docker Engine + Compose)
- Playground sample running on **http://localhost:5289** with `/metrics` exposed

## Start Prometheus

From the repo root:

```bash
docker compose -f deploy/prometheus/docker-compose.yml up -d
```

- Prometheus UI: http://localhost:9090  
- Targets: http://localhost:9090/targets — job `cacheorchestrator-playground` should become **UP** after the playground is running  
- Stop: `docker compose -f deploy/prometheus/docker-compose.yml down`

If you already had an older compose stack scraping port 5290, recreate so the new config loads:

```bash
docker compose -f deploy/prometheus/docker-compose.yml down
docker compose -f deploy/prometheus/docker-compose.yml up -d
```

## Full local stack

```bash
# 1) Prometheus
docker compose -f deploy/prometheus/docker-compose.yml up -d

# 2) Playground (exposes /metrics + Admin API)
dotnet run --project samples/CacheOrchestrator.Sample

# 3) Generate traffic (UI http://localhost:5289 or curl)
curl -i http://localhost:5289/api/catalog

# 4) Admin App (Metrics → http://localhost:9090; Instances → playground)
dotnet run --project src/CacheOrchestrator.Admin
```

Open http://localhost:5188/#/metrics after ~15–30 s of scrapes.

## Troubleshooting

| Symptom | Fix |
|---------|-----|
| Target **DOWN** | Start playground on port **5289**; open http://localhost:5289/metrics in a browser |
| Target DOWN on Linux | `extra_hosts: host.docker.internal:host-gateway` is already set; ensure Docker can reach the host port |
| Admin Metrics **Disconnected** | Prometheus on 9090? `curl http://localhost:9090/-/ready` |
| Charts empty | Hit playground endpoints (e.g. `/api/catalog`); wait one scrape interval (5 s); check Prometheus for `cache_orchestrator_oc_requests_total` |
| Stale scrape target | `docker compose -f deploy/prometheus/docker-compose.yml up -d --force-recreate` |

See also [docs/admin.md](../../docs/admin.md) (Metrics store) and [docs/observability.md](../../docs/observability.md).
