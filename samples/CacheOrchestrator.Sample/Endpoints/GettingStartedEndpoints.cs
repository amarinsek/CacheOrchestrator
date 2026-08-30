using CacheOrchestrator.DataCache;
using CacheOrchestrator.Invalidation;
using CacheOrchestrator.OutputCache;
using CacheOrchestrator.Sample.Data;

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

        app.MapGet("/api/products/{id:int}", async (
            HttpContext http,
            int id,
            IDomainDataCache cache,
            PlaygroundProductStore store,
            CancellationToken cancellationToken) =>
        {
            Product? product = await cache.GetOrSetEntityAsync(http, async token =>
            {
                // Artificial delay so FACTORY stays visible in the playground.
                await Task.Delay(200, token).ConfigureAwait(false);
                PlaygroundProduct? row = await store.GetAsync(id.ToString(), token).ConfigureAwait(false);
                return row is null ? null : new Product(id, row.Name, row.Price);
            }, cancellationToken).ConfigureAwait(false);

            return product is null ? Results.NotFound() : Results.Json(product);
        })
        .CacheOutputWithDomain(
            "catalog",
            entityKind: "products",
            resourceRouteKey: "id");

        app.MapPut("/api/products/{id:int}", async (
            int id,
            UpdateProduct request,
            PlaygroundProductStore store,
            ICacheOrchestratorInvalidator invalidator,
            CancellationToken cancellationToken) =>
        {
            await store.UpsertAsync(
                id.ToString(),
                request.Name,
                request.Price,
                DateTimeOffset.UtcNow,
                cancellationToken).ConfigureAwait(false);

            await invalidator.InvalidateEntityAsync(
                "catalog",
                "products",
                id,
                cancellationToken).ConfigureAwait(false);

            return Results.NoContent();
        });
    }

    public sealed record Product(int Id, string Name, decimal Price);

    public sealed record UpdateProduct(string Name, decimal Price);
}
