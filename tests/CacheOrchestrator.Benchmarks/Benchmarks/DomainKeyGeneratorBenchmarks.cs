using BenchmarkDotNet.Attributes;
using CacheOrchestrator.Configuration;
using CacheOrchestrator.DataCache;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;

namespace CacheOrchestrator.Benchmarks.Benchmarks;

[MemoryDiagnoser]
[ShortJob]
public class DomainKeyGeneratorBenchmarks
{
    private DefaultDomainKeyGenerator _generator = null!;
    private DomainHttpCacheOptions _options = null!;
    private DefaultHttpContext _noQuery = null!;
    private DefaultHttpContext _withQuery = null!;
    private DefaultHttpContext _withTracking = null!;
    private DefaultHttpContext _varyEncoding = null!;
    private DefaultHttpContext _varyHost = null!;
    private DefaultHttpContext _resourceId = null!;
    private DefaultHttpContext _routeEndpoint = null!;

    [GlobalSetup]
    public void Setup()
    {
        _generator = new DefaultDomainKeyGenerator();

        _options = new DomainHttpCacheOptions
        {
            CoreOptions = new DomainCacheOptions
            {
                Domain = "catalog",
                Version = "1",
                VersionHex = "01",
                DataCacheEnabled = true,
                DataCacheTtl = TimeSpan.FromSeconds(60),
                DataCacheNamespace = "sample:fc",
            },
            OutputCacheEnabled = true,
            ClientCacheability = ClientCacheability.Public,
            ClientTtlSeconds = 3600,
            ClientTtlMinSeconds = 60,
            OutputTtl = TimeSpan.FromSeconds(60),
            OutputCacheNamespace = "sample:oc",
            CacheableStatusCodes = [200],
            EncodingNormalizationList = ["br", "gzip"],
            DataCacheVaryOnEncoding = true,
            DataCacheVaryOnPublicAddress = true
        };

        _noQuery = CreateHttp("/api/catalog");
        _withQuery = CreateHttp("/api/catalog", "?page=1&sort=name&filter=active");
        _withTracking = CreateHttp("/api/catalog", "?page=1&utm_source=google&fbclid=abc123");
        _varyEncoding = CreateHttp("/api/catalog", acceptEncoding: "gzip, deflate, br");
        _varyHost = CreateHttp("/api/catalog", host: "cdn.example.com", scheme: "https");

        _resourceId = CreateHttp("/api/products/42");
        _resourceId.Features.Set<ICacheOrchestratorFeature>(new CacheOrchestratorFeature { EntityKind = "products", ResourceId = "42" });

        _routeEndpoint = CreateHttp("/api/products/42");
        RoutePattern pattern = RoutePatternFactory.Parse("/api/products/{id}");
        var endpoint = new RouteEndpoint(
            _ => Task.CompletedTask,
            pattern,
            order: 0,
            new EndpointMetadataCollection(),
            displayName: "products");
        _routeEndpoint.SetEndpoint(endpoint);
        _routeEndpoint.Request.RouteValues["id"] = "42";
    }

    [Benchmark(Baseline = true)]
    public string PathOnly()
        => _generator.Generate(_options, _noQuery);

    [Benchmark]
    public string WithQuery()
        => _generator.Generate(_options, _withQuery);

    [Benchmark]
    public string WithTrackingQuery()
        => _generator.Generate(_options, _withTracking);

    [Benchmark]
    public string WithAcceptEncoding()
        => _generator.Generate(_options, _varyEncoding);

    [Benchmark]
    public string WithHostAndScheme()
        => _generator.Generate(_options, _varyHost);

    [Benchmark]
    public string WithResourceId()
        => _generator.Generate(_options, _resourceId);

    [Benchmark]
    public string WithRouteEndpointAndParam()
        => _generator.Generate(_options, _routeEndpoint);

    private static DefaultHttpContext CreateHttp(
        string path,
        string? query = null,
        string? acceptEncoding = null,
        string host = "localhost",
        string scheme = "http")
    {
        var http = new DefaultHttpContext();
        http.Request.Method = HttpMethods.Get;
        http.Request.Path = path;
        http.Request.Host = new HostString(host);
        http.Request.Scheme = scheme;

        if (!string.IsNullOrEmpty(query))
            http.Request.QueryString = new QueryString(query);

        if (!string.IsNullOrEmpty(acceptEncoding))
            http.Request.Headers.AcceptEncoding = acceptEncoding;

        return http;
    }
}
