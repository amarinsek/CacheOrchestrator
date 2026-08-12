using CacheOrchestrator.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;

namespace CacheOrchestrator.Bus;

/// <summary>
/// Validates <c>X-Cache-Admin-Key</c> for cluster receive endpoints using
/// <c>Cache:Cluster:Bus:ApiKey</c> or fallback <c>Cache:Admin:ApiKey</c>.
/// </summary>
internal sealed class ClusterEndpointAuth : IEndpointFilter
{
    /// <summary>Shared header name with Local Admin API.</summary>
    public const string HeaderName = "X-Cache-Admin-Key";

    private readonly IOptionsMonitor<CacheOrchestratorOptions> _options;

    public ClusterEndpointAuth(IOptionsMonitor<CacheOrchestratorOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    /// <inheritdoc />
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        string? configured = HttpClusterCommandBus.ResolveApiKey(_options.CurrentValue);
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
            CryptographicOperations.FixedTimeEquals(a, a);
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(a, b);
    }
}
