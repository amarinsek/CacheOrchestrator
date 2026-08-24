using CacheOrchestrator.Entity;
using CacheOrchestrator.DataCache;
using CacheOrchestrator.Invalidation;
using CacheOrchestrator.OutputCache;
using Microsoft.AspNetCore.Builder;
using System.Diagnostics;
using System.Text.Json;

namespace CacheOrchestrator.Sample.Endpoints;

/// <summary>Options for a single demo data endpoint loaded from configuration.</summary>
public sealed class DemoEndpointConfig
{
    public string Path { get; set; } = "/api/unknown";
    public string Domain { get; set; } = "default";
    public int DelayMs { get; set; } = 50;
    public string Label { get; set; } = "";
}

public static class DemoEndpoints
{
    /// <summary>
    /// Registers data endpoints dynamically from configuration (Demo:Endpoints[]).
    /// Each endpoint simulates an async data fetch and is cached with the configured domain.
    /// </summary>
    public static void MapDemoDataEndpoints(this WebApplication app, IConfiguration config)
    {
        var entries = config.GetSection("Demo:Endpoints").Get<List<DemoEndpointConfig>>() ?? [];

        foreach (var entry in entries)
        {
            var path = entry.Path;
            var domain = entry.Domain;
            var delayMs = entry.DelayMs;

            app.MapGet(path, async (HttpContext http, IDomainDataCache cache) =>
            {
                var sw = Stopwatch.StartNew();
                var data = await cache.GetOrSetAsync(http, async _ =>
                {
                    await Task.Delay(delayMs);
                    return new
                    {
                        path,
                        domain,
                        generatedAt = DateTimeOffset.UtcNow
                    };
                });
                sw.Stop();
                http.Response.Headers["X-Demo-Elapsed-Ms"] = sw.ElapsedMilliseconds.ToString();
                return Results.Json(data);
            }).CacheOutputWithDomain(domain);
        }
    }

    /// <summary>
    /// Single compact demo for domain vary: <c>VaryByAccept</c> + <c>VaryByQueryKeys: [lang]</c>
    /// under domain <c>vary-demo</c> (<c>AuthBypassMode: Never</c>).
    /// </summary>
    public static void MapVaryDemoEndpoint(this WebApplication app)
    {
        const string domain = "vary-demo";
        const string note =
            "Change Accept and/or ?lang= — OC/FC should MISS across variants; utm_* and other query keys are ignored (allowlist).";

        app.MapGet("/api/vary-demo", async (HttpContext http, IDomainDataCache cache) =>
        {
            var sw = Stopwatch.StartNew();
            VaryDemoPayload data = await cache.GetOrSetAsync(http, async _ =>
            {
                await Task.Delay(40);
                string lang = http.Request.Query.TryGetValue("lang", out var lv) && lv.Count > 0
                    ? lv.ToString()
                    : "en";
                return new VaryDemoPayload(lang, DateTimeOffset.UtcNow);
            });
            sw.Stop();
            http.Response.Headers["X-Demo-Elapsed-Ms"] = sw.ElapsedMilliseconds.ToString();

            string accept = http.Request.Headers.Accept.ToString();
            bool wantXml = accept.Contains("xml", StringComparison.OrdinalIgnoreCase);
            if (wantXml)
            {
                string xml =
                    $"<varyDemo domain=\"{domain}\" lang=\"{System.Security.SecurityElement.Escape(data.Lang)}\" " +
                    $"generatedAt=\"{data.GeneratedAt:O}\"><note>{System.Security.SecurityElement.Escape(note)}</note></varyDemo>";
                return Results.Content(xml, "application/xml");
            }

            return Results.Json(new
            {
                domain,
                representation = "json",
                lang = data.Lang,
                accept,
                note,
                generatedAt = data.GeneratedAt,
            });
        }).CacheOutputWithDomain(domain);
    }

    private sealed record VaryDemoPayload(string Lang, DateTimeOffset GeneratedAt);

    /// <summary>Studio control APIs for the demo UI.</summary>
    public static void MapDemoStudioEndpoints(this WebApplication app)
    {
        // CacheOrchestrator registers an Output Cache *base* policy for all endpoints.
        // Control APIs must opt out (same pattern as Admin /metrics), or GET /appsettings
        // keeps serving a stale OC body while the file on disk is already updated.
        static TBuilder NoOutputCache<TBuilder>(TBuilder builder)
            where TBuilder : IEndpointConventionBuilder
            => builder.WithMetadata(new Microsoft.AspNetCore.OutputCaching.OutputCacheAttribute { NoStore = true });

        NoOutputCache(app.MapGet("/api/demo/endpoints", (IConfiguration config, CacheOrchestrator.Configuration.IDomainCacheOptionsProvider provider) =>
        {
            var entries = config.GetSection("Demo:Endpoints").Get<List<DemoEndpointConfig>>() ?? [];
            string BackendFor(string domain)
            {
                var opts = provider.GetOrCreateDomainOptions(domain);
                var fcName = opts.DataCacheInstanceName ?? "default";
                var fcProvider = config[$"Cache:DataCacheInstances:{fcName}:Provider"] ?? "InMemory";
                var ocProvider = config["Cache:OutputCache:Provider"] ?? "InMemory";
                return $"{ocProvider} / {fcProvider}";
            }

            // Config-driven demo routes only (CRUD is a separate UI panel).
            var fromConfig = entries.Select(e => new
            {
                url = e.Path,
                domain = e.Domain,
                label = string.IsNullOrWhiteSpace(e.Label) ? e.Path : e.Label,
                backend = BackendFor(e.Domain),
                method = "GET",
                source = "config",
            });

            // Vary demo (Accept + lang query) — listed with config routes so it appears in the domain panel.
            var varyMeta = new[]
            {
                new
                {
                    url = "/api/vary-demo",
                    domain = "vary-demo",
                    label = "Vary demo (Accept + ?lang=)",
                    backend = BackendFor("vary-demo"),
                    method = "GET",
                    source = "config",
                },
            };

            // Metadata for the Entity invalidation panel (not mixed into the domain dropdown).
            var crudMeta = new[]
            {
                new
                {
                    url = "/api/crud/products/{id}",
                    domain = "product-crud",
                    label = "CRUD product",
                    backend = BackendFor("product-crud"),
                    method = "GET",
                    source = "hardcoded",
                },
            };

            return Results.Json(fromConfig.Concat(varyMeta).Concat(crudMeta));
        }));

        // Returns the raw appsettings.json content for the JSON editor.
        NoOutputCache(app.MapGet("/api/demo/appsettings", (IWebHostEnvironment env) =>
        {
            var path = Path.Combine(env.ContentRootPath, "appsettings.json");
            var content = File.ReadAllText(path);
            return Results.Text(content, "application/json");
        }));

        // Saves the appsettings.json from the JSON editor. Validates JSON first.
        NoOutputCache(app.MapPut("/api/demo/appsettings", async (
            HttpRequest request,
            IWebHostEnvironment env,
            IConfiguration config) =>
        {
            using var reader = new StreamReader(request.Body);
            var raw = await reader.ReadToEndAsync();

            // Validate JSON before saving
            try
            {
                JsonDocument.Parse(raw);
            }
            catch (JsonException ex)
            {
                return Results.Problem(
                    title: "Invalid JSON",
                    detail: ex.Message,
                    statusCode: 400);
            }

            var path = Path.Combine(env.ContentRootPath, "appsettings.json");

            try
            {
                var doc = JsonDocument.Parse(raw);
                var formatted = JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(path, formatted);
            }
            catch (Exception ex)
            {
                return Results.Problem(
                    title: "Failed to save appsettings.json",
                    detail: ex.Message,
                    statusCode: 500);
            }

            // Multi-instance labs: bind mounts may not raise FileSystemWatcher — apply this write now.
            // Reload only: new Version/TTL/client headers apply on the next request via options snapshot.
            // Do not invalidate here — purge is a separate playground action (Invalidate domain / entity).
            if (config is IConfigurationRoot root)
                root.Reload();

            return Results.Ok(new { saved = true, at = DateTimeOffset.UtcNow });
        }));

        NoOutputCache(app.MapPost("/api/demo/invalidate/{domain}", async (string domain, ICacheOrchestratorInvalidator inv) =>
        {
            await inv.InvalidateDomainAsync(domain);
            return Results.Ok(new { invalidated = domain, at = DateTimeOffset.UtcNow });
        }));

        // Purge one entity tag only (playground CRUD demo) — does not change the in-memory "DB".
        NoOutputCache(app.MapPost("/api/demo/invalidate-entity/{domain}/{entityKind}/{id}", async (
            string domain,
            string entityKind,
            string id,
            ICacheOrchestratorInvalidator inv) =>
        {
            var result = await inv.InvalidateEntityAsync(domain, entityKind, id);
            return Results.Ok(new
            {
                invalidatedEntity = new { domain, entityKind, id },
                result.Tags,
                at = DateTimeOffset.UtcNow,
                tip = "GET the entity again — server MISS if browser cache is off; body still old until PUT updates the store."
            });
        }));

        // --- Dynamic CRUD demo (entity invalidation under a stable Version) ---
        // In-memory "DB" for the playground only.
        var productStore = new System.Collections.Concurrent.ConcurrentDictionary<string, ProductRecord>(StringComparer.OrdinalIgnoreCase)
        {
            ["42"] = new ProductRecord("42", "Demo Widget", 10.00m, DateTimeOffset.UtcNow),
            ["7"] = new ProductRecord("7", "Sample Gadget", 19.50m, DateTimeOffset.UtcNow)
        };

        app.MapGet("/api/crud/products/{id}", async (HttpContext http, string id, IDomainDataCache cache) =>
        {
            var product = await cache.GetOrSetEntityAsync(http, async ct =>
            {
                await Task.Delay(40, ct);
                if (!productStore.TryGetValue(id, out var row))
                    return null;
                return new
                {
                    row.Id,
                    row.Name,
                    row.Price,
                    row.UpdatedAt,
                    loadedAt = DateTimeOffset.UtcNow
                };
            });

            return product is null ? Results.NotFound() : Results.Json(product);
        }).CacheOutputWithDomain("product-crud", resourceRouteKey: "id", entityKind: "products");

        app.MapPut("/api/crud/products/{id}", async (
            string id,
            ProductUpdateDto body,
            ICacheOrchestratorInvalidator inv) =>
        {
            var updated = new ProductRecord(
                id,
                string.IsNullOrWhiteSpace(body.Name) ? $"Product {id}" : body.Name!,
                body.Price,
                DateTimeOffset.UtcNow);

            productStore[id] = updated;

            // Same Version — only this entity is purged from OC + FC.
            await inv.InvalidateEntityAsync("product-crud", "products", id);
            return Results.Json(new
            {
                saved = true,
                product = updated,
                invalidatedEntity = id,
                tip = "GET /api/crud/products/" + id + " again — should be MISS then new price"
            });
        });

        app.MapGet("/api/crud/products", () =>
            Results.Json(productStore.Values.OrderBy(p => p.Id)));
    }

    private sealed record ProductRecord(string Id, string Name, decimal Price, DateTimeOffset UpdatedAt);

    private sealed class ProductUpdateDto
    {
        public string? Name { get; set; }
        public decimal Price { get; set; }
    }
}
