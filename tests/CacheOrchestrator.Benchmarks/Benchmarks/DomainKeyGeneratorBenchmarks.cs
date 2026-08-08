using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using CacheOrchestrator.Configuration;
using CacheOrchestrator.FusionCache;
using Microsoft.AspNetCore.Http;

namespace CacheOrchestrator.Benchmarks.Benchmarks;

[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net10_0, warmupCount: 1, iterationCount: 3, launchCount: 1)]
public class DomainKeyGeneratorBenchmarks
{
    private DefaultDomainKeyGenerator _generator = null!;
    private DomainCacheOptions _options = null!;
    private DefaultHttpContext _noQuery = null!;
    private DefaultHttpContext _withQuery = null!;
    private DefaultHttpContext _withTracking = null!;
    private DefaultHttpContext _varyEncoding = null!;
    private DefaultHttpContext _varyHost = null!;

    [GlobalSetup]
    public void Setup()
    {
        _generator = new DefaultDomainKeyGenerator();

        _options = new DomainCacheOptions
        {
            Domain = "catalog",
            Version = "1",
            OutputCacheEnabled = true,
            FusionCacheEnabled = true,
            ClientCacheability = ClientCacheability.Public,
            ClientTtlSeconds = 3600,
            ClientTtlMinSeconds = 60,
            OutputTtl = TimeSpan.FromSeconds(60),
            FusionCacheSoftTtl = TimeSpan.FromSeconds(60),
            FusionCacheHardTtl = TimeSpan.FromHours(12),
            FusionCacheFailSafe = TimeSpan.FromHours(24),
            OutputCacheNamespace = "sample:oc",
            FusionCacheNamespace = "sample:fc",
            CacheableStatusCodes = [200],
            EncodingNormalizationList = ["br", "gzip"],
            FusionCacheVaryOnEncoding = true,
            FusionCacheVaryOnPublicAddress = true
        };

        _noQuery = CreateHttp("/api/catalog");
        _withQuery = CreateHttp("/api/catalog", "?page=1&sort=name&filter=active");
        _withTracking = CreateHttp("/api/catalog", "?page=1&utm_source=google&fbclid=abc123");
        _varyEncoding = CreateHttp("/api/catalog", acceptEncoding: "gzip, deflate, br");
        _varyHost = CreateHttp("/api/catalog", host: "cdn.example.com", scheme: "https");
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

        // No RouteEndpoint -> falls through to path branch of generator (typical Minimal API without pattern in tests)
        return http;
    }
}