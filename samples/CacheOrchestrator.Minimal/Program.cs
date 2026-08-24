using CacheOrchestrator.DependencyInjection;
using CacheOrchestrator.Entity;
using CacheOrchestrator.FusionCache;
using CacheOrchestrator.OutputCache;

var builder = WebApplication.CreateBuilder(args);

// InMemory only — no Redis package required for this sample.
builder.Services.AddCacheOrchestrator(builder.Configuration);

var app = builder.Build();

app.UseCacheOrchestrator();
app.MapCacheOrchestratorAdmin(); // no-op unless Cache:Admin:Enabled

// Domain rules live in appsettings (Cache:Domains:hello).
// One line on the endpoint wires Output Cache + FusionCache for this route.
app.MapGet("/hello", async (HttpContext http, IDomainFusionCache cache) =>
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
      <p>Call <a href="/hello"><code>/hello</code></a> twice and compare the <code>X-Cache</code> header.</p>
      <ol>
        <li>First request → <code>oc=miss</code> (or <code>fc=miss; fa=run</code>) — factory runs (~200&nbsp;ms).</li>
        <li>Second request → <code>oc=hit</code> — served from Output Cache (fast).</li>
      </ol>
      <pre>curl -i http://localhost:5290/hello</pre>
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
Console.WriteLine("  Watch  X-Cache: ... oc=miss  then  oc=hit");
Console.WriteLine();

app.Run();
