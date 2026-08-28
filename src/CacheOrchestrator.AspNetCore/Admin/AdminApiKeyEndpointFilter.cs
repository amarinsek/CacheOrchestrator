using CacheOrchestrator.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;

namespace CacheOrchestrator.Admin;

/// <summary>
/// Validates <c>X-Cache-Admin-Key</c> when the ASP.NET Core Admin API key is set.
/// </summary>
internal sealed class AdminApiKeyEndpointFilter : IEndpointFilter
{
    /// <summary>Header name for the shared admin API key.</summary>
    public const string HeaderName = "X-Cache-Admin-Key";

    private readonly IOptionsMonitor<CacheOrchestratorHttpOptions> _options;

    public AdminApiKeyEndpointFilter(IOptionsMonitor<CacheOrchestratorHttpOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    /// <inheritdoc />
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        string? configured = _options.CurrentValue.Admin.ApiKey;
        if (string.IsNullOrEmpty(configured))
            return await next(context).ConfigureAwait(false);

        HttpContext http = context.HttpContext;
        if (!http.Request.Headers.TryGetValue(HeaderName, out Microsoft.Extensions.Primitives.StringValues provided)
            || provided.Count == 0
            || !FixedTimeEquals(configured, provided.ToString()))
        {
            return Results.Unauthorized();
        }

        return await next(context).ConfigureAwait(false);
    }

    private static bool FixedTimeEquals(string expected, string actual)
    {
        byte[] a = Encoding.UTF8.GetBytes(expected);
        byte[] b = Encoding.UTF8.GetBytes(actual);
        if (a.Length != b.Length)
        {
            // Still compare to reduce timing oracle on length alone (dummy).
            CryptographicOperations.FixedTimeEquals(a, a);
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(a, b);
    }
}
