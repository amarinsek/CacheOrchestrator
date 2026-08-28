using CacheOrchestrator.DataCache;
using CacheOrchestrator.Invalidation;
using CacheOrchestrator.OutputCache;
using System.Collections.Concurrent;

namespace CacheOrchestrator.Sample.Endpoints;

/// <summary>The runnable version of the guide/getting-started example.</summary>
public static class GettingStartedEndpoints
{
    /// <summary>Maps the promotions and product endpoints used in the Getting started guide.</summary>
    public static void MapGettingStartedEndpoints(this WebApplication app)
    {
        app.MapGet("/api/promotions", () => new
        {
            Title = "Summer sale",
            DiscountPercent = 20,
            GeneratedAtUtc = DateTimeOffset.UtcNow
        })
        .CacheOutputWithDomain("promotions");

        ConcurrentDictionary<int, Product> products = new(
            new[]
            {
                new KeyValuePair<int, Product>(42, new(42, "Demo Widget", 10.00m)),
                new KeyValuePair<int, Product>(7, new(7, "Sample Gadget", 19.50m))
            });

        app.MapGet("/api/products/{id:int}", async (
            HttpContext http,
            int id,
            IDomainDataCache cache,
            CancellationToken cancellationToken) =>
        {
            Product? product = await cache.GetOrSetEntityAsync(http, async token =>
            {
                // Pretend this is a database or remote-service call.
                await Task.Delay(200, token);
                products.TryGetValue(id, out Product? value);
                return value;
            }, cancellationToken);

            return product is null ? Results.NotFound() : Results.Json(product);
        })
        .CacheOutputWithDomain(
            "catalog",
            entityKind: "products",
            resourceRouteKey: "id");

        app.MapPut("/api/products/{id:int}", async (
            int id,
            UpdateProduct request,
            ICacheOrchestratorInvalidator invalidator,
            CancellationToken cancellationToken) =>
        {
            products[id] = new Product(id, request.Name, request.Price);

            await invalidator.InvalidateEntityAsync(
                "catalog",
                "products",
                id,
                cancellationToken);

            return Results.NoContent();
        });
    }

    public sealed record Product(int Id, string Name, decimal Price);

    public sealed record UpdateProduct(string Name, decimal Price);
}
