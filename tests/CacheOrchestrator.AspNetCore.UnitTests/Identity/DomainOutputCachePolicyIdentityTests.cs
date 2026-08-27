using CacheOrchestrator.Configuration;
using CacheOrchestrator.Identity;
using CacheOrchestrator.OutputCache;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Primitives;
using System.IO.Hashing;
using System.IO.Pipelines;
using System.Text;

namespace CacheOrchestrator.AspNetCore.UnitTests.Identity;

public class DomainOutputCachePolicyIdentityTests
{
    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    public async Task CacheRequestAsync_WithoutIdentity_NonGetHead_DoesNotEnable(string method)
    {
        DomainOutputCachePolicy policy = new("products");
        (OutputCacheContext context, _) = CreateContext(method: method);

        await policy.CacheRequestAsync(context, CancellationToken.None);

        context.EnableOutputCaching.Should().BeFalse();
    }

    [Fact]
    public async Task CacheRequestAsync_WithoutIdentity_Get_StillEnables()
    {
        DomainOutputCachePolicy policy = new("products");
        (OutputCacheContext context, _) = CreateContext(method: "GET");

        await policy.CacheRequestAsync(context, CancellationToken.None);

        context.EnableOutputCaching.Should().BeTrue();
    }

    [Fact]
    public async Task CacheRequestAsync_WithIdentity_PostBound_Enables()
    {
        DomainOutputCachePolicy policy = new("products");
        CacheIdentityEndpointMetadata identity = new();
        identity.AddBinding("POST", CacheIdentityBinding.CreateUrl(), "test");

        (OutputCacheContext context, _) = CreateContext(method: "POST", identity: identity);

        await policy.CacheRequestAsync(context, CancellationToken.None);

        context.EnableOutputCaching.Should().BeTrue();
    }

    [Fact]
    public async Task CacheRequestAsync_WithIdentity_UnboundMethod_DoesNotEnable()
    {
        DomainOutputCachePolicy policy = new("products");
        CacheIdentityEndpointMetadata identity = new();
        identity.AddBinding("POST", CacheIdentityBinding.CreateUrl(), "test");

        (OutputCacheContext context, _) = CreateContext(method: "PUT", identity: identity);

        await policy.CacheRequestAsync(context, CancellationToken.None);

        context.EnableOutputCaching.Should().BeFalse();
    }

    [Fact]
    public async Task CacheRequestAsync_WithContract_AppliesVaryValues()
    {
        DomainOutputCachePolicy policy = new("products");
        var binding = CacheIdentityBinding.CreateNamed("search-v1");
        binding.SetContract(new FixedContract("search-v1", new CacheIdentityMaterial(
        [
            new KeyValuePair<string, string>("q", "widgets"),
        ])));

        CacheIdentityEndpointMetadata identity = new();
        identity.AddBinding("POST", binding, "test");
        identity.MarkResolved();

        (OutputCacheContext context, _) = CreateContext(method: "POST", identity: identity);

        await policy.CacheRequestAsync(context, CancellationToken.None);

        context.EnableOutputCaching.Should().BeTrue();
        context.CacheVaryByRules.VaryByValues["co-id:q"].ToString().Should().Be("widgets");
    }

    [Fact]
    public async Task CacheRequestAsync_WhenContractReturnsNull_DoesNotEnable()
    {
        DomainOutputCachePolicy policy = new("products");
        var binding = CacheIdentityBinding.CreateNamed("search-v1");
        binding.SetContract(new FixedContract("search-v1", material: null));

        CacheIdentityEndpointMetadata identity = new();
        identity.AddBinding("POST", binding, "test");
        identity.MarkResolved();

        (OutputCacheContext context, DefaultHttpContext http) = CreateContext(method: "POST", identity: identity);

        await policy.CacheRequestAsync(context, CancellationToken.None);

        context.EnableOutputCaching.Should().BeFalse();
        var feature = (CacheOrchestratorFeature)http.Features.Get<ICacheOrchestratorFeature>()!;
        feature.IdentityBypass.Should().BeTrue();
    }

    [Fact]
    public async Task CacheRequestAsync_ContentHash_DistinctBodies_DistinctVary()
    {
        DomainOutputCachePolicy policy = new("products");
        CacheIdentityEndpointMetadata identity = new();
        identity.AddBinding("POST", CacheIdentityBinding.CreateContentHash(65_536), "test");

        (OutputCacheContext a, _) = CreateContext(method: "POST", identity: identity, body: "{\"q\":1}");
        (OutputCacheContext b, _) = CreateContext(method: "POST", identity: identity, body: "{\"q\":2}");

        await policy.CacheRequestAsync(a, CancellationToken.None);
        await policy.CacheRequestAsync(b, CancellationToken.None);

        a.EnableOutputCaching.Should().BeTrue();
        b.EnableOutputCaching.Should().BeTrue();
        a.CacheVaryByRules.VaryByValues["co-id:body-hash"].ToString()
            .Should().NotBe(b.CacheVaryByRules.VaryByValues["co-id:body-hash"].ToString());
    }

    [Fact]
    public async Task CacheRequestAsync_ContentHash_Oversize_DoesNotEnable()
    {
        DomainOutputCachePolicy policy = new("products");
        CacheIdentityEndpointMetadata identity = new();
        identity.AddBinding("POST", CacheIdentityBinding.CreateContentHash(8), "test");

        (OutputCacheContext context, _) = CreateContext(
            method: "POST",
            identity: identity,
            body: "0123456789");

        await policy.CacheRequestAsync(context, CancellationToken.None);

        context.EnableOutputCaching.Should().BeFalse();
    }

    private static (OutputCacheContext context, DefaultHttpContext http) CreateContext(
        string method = "GET",
        CacheIdentityEndpointMetadata? identity = null,
        string? body = null)
    {
        DefaultHttpContext http = new();
        OnStartingResponseFeature responseFeature = new();
        http.Features.Set<IHttpResponseFeature>(responseFeature);
        http.Features.Set<IHttpResponseBodyFeature>(responseFeature);
        http.Request.Method = method;
        http.Request.Path = "/api/products";

        if (body is not null)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(body);
            http.Request.Body = new MemoryStream(bytes);
            http.Request.ContentLength = bytes.Length;
        }

        if (identity is not null)
        {
            List<object> metadata = [identity];
            Endpoint endpoint = new(_ => Task.CompletedTask, new EndpointMetadataCollection(metadata), "test");
            http.SetEndpoint(endpoint);
        }

        DomainCacheOptions cfg = new()
        {
            Domain = "products",
            OutputCacheEnabled = true,
            AuthBypassMode = AuthBypassMode.AuthenticatedOrAuthorization,
            VaryOutputCacheByUser = true,
            OutputTtl = TimeSpan.FromSeconds(60),
            Version = "1",
            VersionHex = XxHash3.HashToUInt64("1"u8.ToArray()).ToString("x16"),
            ETag = new StringValues($"W/\"{XxHash3.HashToUInt64("1"u8.ToArray()):x16}\""),
            CacheableStatusCodes = [200],
            ClientCacheability = ClientCacheability.Public,
            ClientTtlSeconds = 60,
            ClientTtlMinSeconds = 60,
            OutputCacheNamespace = "test-oc",
            ClientForcePrivateWhenAuthenticated = true,
        };

        IRequestDomainCacheOptions domainConfig = Substitute.For<IRequestDomainCacheOptions>();
        domainConfig.EnsureDomainOptions(http, Arg.Any<string>()).Returns(call =>
        {
            http.Features.Set<ICacheOrchestratorFeature>(new CacheOrchestratorFeature { DomainOptions = cfg });
            return cfg;
        });

        ServiceCollection services = new();
        services.AddSingleton(domainConfig);
        services.AddSingleton(typeof(ILogger<DomainOutputCachePolicy>), NullLogger<DomainOutputCachePolicy>.Instance);
        services.AddSingleton(TimeProvider.System);
        http.RequestServices = services.BuildServiceProvider();

        OutputCacheContext context = new()
        {
            HttpContext = http,
            EnableOutputCaching = true,
        };

        return (context, http);
    }

    private sealed class FixedContract(string name, CacheIdentityMaterial? material) : ICacheIdentityContract
    {
        public string Name { get; } = name;

        public ValueTask<CacheIdentityMaterial?> BuildAsync(
            CacheIdentityContext context,
            CancellationToken cancellationToken)
            => ValueTask.FromResult(material);
    }

    private sealed class OnStartingResponseFeature : IHttpResponseFeature, IHttpResponseBodyFeature
    {
        private readonly List<(Func<object, Task> Callback, object State)> _onStarting = [];

        public int StatusCode { get; set; } = 200;
        public string? ReasonPhrase { get; set; }
        public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();
        public Stream Body { get; set; } = new MemoryStream();
        public bool HasStarted { get; private set; }
        public Stream Stream => Body;
        public PipeWriter Writer => field ??= PipeWriter.Create(Body);

        public void OnStarting(Func<object, Task> callback, object state)
        {
            ArgumentNullException.ThrowIfNull(callback);
            _onStarting.Add((callback, state));
        }

        public void OnCompleted(Func<object, Task> callback, object state)
        {
        }

        public async Task StartAsync(CancellationToken cancellationToken = default)
        {
            if (HasStarted)
                return;

            for (int i = _onStarting.Count - 1; i >= 0; i--)
            {
                (Func<object, Task> callback, object state) = _onStarting[i];
                await callback(state);
            }

            HasStarted = true;
        }

        public Task CompleteAsync() => Task.CompletedTask;

        public void DisableBuffering()
        {
        }

        public Task SendFileAsync(string path, long offset, long? count, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
