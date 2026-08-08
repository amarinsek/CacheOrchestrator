using CacheOrchestrator.DependencyInjection;
using CacheOrchestrator.Redis;
using CacheOrchestrator.Sample.Endpoints;

var builder = WebApplication.CreateBuilder(args);

// InMemory is always available; AddRedisBackend enables "Provider": "Redis" in appsettings.
builder.Services.AddCacheOrchestrator(builder.Configuration, o => o.AddRedisBackend());

var app = builder.Build();

app.Use(async (ctx, next) =>
{
    ctx.Response.Headers["X-Demo-Hit-Id"] = Guid.NewGuid().ToString("N");
    await next();
});

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseCacheOrchestrator();

app.MapDemoDataEndpoints(builder.Configuration);
app.MapDemoStudioEndpoints();

app.MapFallbackToFile("index.html");
app.Run();