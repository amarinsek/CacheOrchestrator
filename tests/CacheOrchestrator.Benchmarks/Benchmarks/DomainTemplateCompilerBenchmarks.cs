using BenchmarkDotNet.Attributes;
using CacheOrchestrator.Utilities;
using Microsoft.AspNetCore.Http;

namespace CacheOrchestrator.Benchmarks.Benchmarks;

[MemoryDiagnoser]
[ShortJob]
public class DomainTemplateCompilerBenchmarks
{
    private Func<HttpContext, string> _staticResolver = null!;
    private Func<HttpContext, string> _hostResolver = null!;
    private Func<HttpContext, string> _combinedResolver = null!;
    private DefaultHttpContext _http = null!;

    [GlobalSetup]
    public void Setup()
    {
        _staticResolver = DomainTemplateCompiler.GetOrAdd("product-catalog");
        _hostResolver = DomainTemplateCompiler.GetOrAdd("tenant-{host}");
        _combinedResolver = DomainTemplateCompiler.GetOrAdd("maps-{host}-{route:z}");

        _http = new DefaultHttpContext();
        _http.Request.Host = new HostString("shop.example.com");
        _http.Request.RouteValues["z"] = "12";
    }

    [Benchmark(Baseline = true)]
    public Func<HttpContext, string> GetOrAdd_Static_Cached()
        => DomainTemplateCompiler.GetOrAdd("product-catalog");

    [Benchmark]
    public Func<HttpContext, string> GetOrAdd_HostTemplate_Cached()
        => DomainTemplateCompiler.GetOrAdd("tenant-{host}");

    [Benchmark]
    public string Resolve_Static()
        => _staticResolver(_http);

    [Benchmark]
    public string Resolve_Host()
        => _hostResolver(_http);

    [Benchmark]
    public string Resolve_Combined()
        => _combinedResolver(_http);
}
