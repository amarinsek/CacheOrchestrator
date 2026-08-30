using CacheOrchestrator.Cluster;
using CacheOrchestrator.Invalidation;
using System.Text.Json;

namespace CacheOrchestrator.Core.UnitTests.Invalidation;

/// <summary>
/// Admin Console AdminApiClient deserializes <see cref="CacheInvalidationResult"/> with Web defaults.
/// Constructor parameter names must match JSON property names (e.g. isSkipped).
/// </summary>
public class CacheInvalidationResultJsonTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Roundtrip_WebDefaults_PreservesFlags()
    {
        var original = new CacheInvalidationResult(
            scope: "domain:store",
            tags: ["domain:store"],
            dataCacheSucceeded: true,
            outputSucceeded: true,
            errors: ["warn"],
            clusterPublish: ClusterPublishResult.Empty,
            isSkipped: false);

        string json = JsonSerializer.Serialize(original, Web);
        CacheInvalidationResult? back = JsonSerializer.Deserialize<CacheInvalidationResult>(json, Web);

        back.Should().NotBeNull();
        back.Scope.Should().Be("domain:store");
        back.Tags.Should().Equal("domain:store");
        back.DataCacheSucceeded.Should().BeTrue();
        back.OutputSucceeded.Should().BeTrue();
        back.IsSkipped.Should().BeFalse();
        back.Succeeded.Should().BeTrue();
        back.Errors.Should().Equal("warn");
        back.ClusterPublish.Should().NotBeNull();
        back.ClusterPublish.Peers.Should().BeEmpty();
    }

    [Fact]
    public void Roundtrip_Skipped_WebDefaults()
    {
        var original = CacheInvalidationResult.Skipped("empty");
        string json = JsonSerializer.Serialize(original, Web);
        CacheInvalidationResult? back = JsonSerializer.Deserialize<CacheInvalidationResult>(json, Web);

        back.Should().NotBeNull();
        back.IsSkipped.Should().BeTrue();
        back.Succeeded.Should().BeFalse();
        back.Errors.Should().ContainSingle().Which.Should().Be("empty");
    }

    [Fact]
    public void Roundtrip_Aggregate_PreservesParts()
    {
        CacheInvalidationResult original = CacheInvalidationResult.Aggregate(
        [
            new CacheInvalidationResult("products", ["domain:products"], true, true),
            new CacheInvalidationResult("catalog", ["domain:catalog"], true, false, ["failed"])
        ]);

        string json = JsonSerializer.Serialize(original, Web);
        CacheInvalidationResult? back = JsonSerializer.Deserialize<CacheInvalidationResult>(json, Web);

        back.Should().NotBeNull();
        back.Parts.Should().HaveCount(2);
        back.Parts[0].Scope.Should().Be("products");
        back.Parts[1].OutputSucceeded.Should().BeFalse();
    }
}
