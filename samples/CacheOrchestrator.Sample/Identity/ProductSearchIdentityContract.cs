using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using CacheOrchestrator.Identity;
using Microsoft.AspNetCore.Http;

namespace CacheOrchestrator.Sample.Identity;

/// <summary>
/// Demo contract: POST search identity is normalized q + sort + page.
/// <c>uiHint</c> in the body is ignored on purpose (must not fragment the cache).
/// </summary>
public sealed class ProductSearchIdentityContract : ICacheIdentityContract
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public string Name => "product-search-v1";

    public async ValueTask<CacheIdentityMaterial?> BuildAsync(
        CacheIdentityContext context,
        CancellationToken cancellationToken)
    {
        HttpRequest request = context.HttpContext.Request;
        request.EnableBuffering();

        SearchBody? body;
        try
        {
            body = await JsonSerializer
                .DeserializeAsync<SearchBody>(request.Body, JsonOptions, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (JsonException)
        {
            return null;
        }
        finally
        {
            if (request.Body.CanSeek)
                request.Body.Position = 0;
        }

        string? q = body?.Q;
        if (string.IsNullOrWhiteSpace(q))
            return null;

        string sort = string.IsNullOrWhiteSpace(body?.Sort) ? "relevance" : body!.Sort!;
        int page = body?.Page is > 0 ? body.Page.Value : 1;

        return new CacheIdentityMaterial(
        [
            new("q", q.Trim().ToLowerInvariant()),
            new("sort", sort.Trim().ToLowerInvariant()),
            new("page", page.ToString(CultureInfo.InvariantCulture)),
        ]);
    }

    private sealed class SearchBody
    {
        [JsonPropertyName("q")]
        public string? Q { get; set; }

        [JsonPropertyName("sort")]
        public string? Sort { get; set; }

        [JsonPropertyName("page")]
        public int? Page { get; set; }

        [JsonPropertyName("uiHint")]
        public string? UiHint { get; set; }
    }
}
