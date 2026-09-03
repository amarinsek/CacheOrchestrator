using CacheOrchestrator.DataCache;
using CacheOrchestrator.DependencyInjection;
using CacheOrchestrator.Identity;
using CacheOrchestrator.OutputCache;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// InMemory only — no Redis package required for this sample.
builder.Services.AddCacheOrchestratorAspNetCore(builder.Configuration);
builder.Services.AddCacheOrchestratorFusionCache(builder.Configuration);

WebApplication app = builder.Build();

app.UseCacheOrchestrator();
app.MapCacheOrchestratorAdmin(); // no-op unless Cache:Admin:Enabled

// Domain rules live in appsettings (Cache:Domains:hello).
// One line on the endpoint wires Output Cache + data cache for this route.
app.MapGet("/hello", async (HttpContext http, IDomainDataCache cache) =>
{
    var payload = await cache.GetOrSetAsync(http, async ct =>
    {
        // Pretend this is a slow DB / service call.
        await Task.Delay(200, ct);
        return new
        {
            Message = "Hello from CacheOrchestrator",
            GeneratedAtUtc = DateTimeOffset.UtcNow
        };
    });

    return Results.Json(payload);
})
.CacheOutputWithDomain("hello");

// Read-only POST with bounded body hash (GraphQL / search style).
// Without WithContentHashCacheIdentity, POST would not be Output Cached.
app.MapPost("/echo", async (HttpContext http) =>
{
    using StreamReader reader = new(http.Request.Body);
    string body = await reader.ReadToEndAsync(http.RequestAborted);
    return Results.Json(new
    {
        Echo = body,
        GeneratedAtUtc = DateTimeOffset.UtcNow
    });
})
.CacheOutputWithDomain("hello")
.WithContentHashCacheIdentity(["POST"], maxBodyBytes: 65_536);

app.MapGet("/", () => Results.Content(
    """
    <!DOCTYPE html>
    <html lang="en">
    <head><meta charset="utf-8"/><title>CacheOrchestrator Minimal</title>
    <style>
      body { font-family: system-ui, sans-serif; max-width: 40rem; margin: 2rem auto; line-height: 1.5; }
      code, pre { background: #f4f4f5; padding: 0.15rem 0.4rem; border-radius: 4px; }
      pre { padding: 0.75rem 1rem; overflow-x: auto; }
      .ok { color: #0a7; font-weight: 600; }
    </style>
    </head>
    <body>
      <h1>CacheOrchestrator · Minimal sample</h1>
      <p>Call <a href="/hello"><code>/hello</code></a> twice and compare the <code>X-CacheOrchestrator</code> header.</p>
      <ol>
        <li>First request → <code>oc=miss</code> (or <code>dc=miss; fa=run</code>) — factory runs (~200&nbsp;ms).</li>
        <li>Second request → <code>oc=hit</code> — served from Output Cache (fast).</li>
      </ol>
      <pre>curl -i http://localhost:5290/hello</pre>
      <p>Optional: POST body identity on <code>/echo</code> (same body → hit):</p>
      <pre>curl -i -X POST http://localhost:5290/echo -H "Content-Type: text/plain" -d "ping"</pre>
      <p class="ok">Tip: use curl (or DevTools → Disable cache) so the browser does not hide server hits.</p>
    </body>
    </html>
    """,
    "text/html"));

const string baseUrl = "http://localhost:5290";
Console.WriteLine();
Console.WriteLine("  CacheOrchestrator Minimal sample");
Console.WriteLine("  --------------------------------");
Console.WriteLine($"  Open  {baseUrl}/");
Console.WriteLine($"  Then  curl -i {baseUrl}/hello   (run twice)");
Console.WriteLine("  Watch  X-CacheOrchestrator: ... oc=miss  then  oc=hit");
Console.WriteLine($"  Optional POST identity: curl -i -X POST {baseUrl}/echo -d ping   (run twice)");
Console.WriteLine();

app.Run();
