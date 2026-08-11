using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;

namespace CacheOrchestrator.Benchmarks.Benchmarks;

/// <summary>Shared BDN job settings for all CacheOrchestrator micro-benchmarks.</summary>
public static class BenchmarkJobs
{
    /// <summary>Short job for local iteration (net10.0).</summary>
    public const int WarmupCount = 1;
    public const int IterationCount = 3;
    public const int LaunchCount = 1;
}

/// <summary>Apply consistent short <see cref="SimpleJobAttribute"/> defaults.</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class ShortJobAttribute : SimpleJobAttribute
{
    public ShortJobAttribute()
        : base(RuntimeMoniker.Net10_0, warmupCount: BenchmarkJobs.WarmupCount, iterationCount: BenchmarkJobs.IterationCount, launchCount: BenchmarkJobs.LaunchCount)
    {
    }
}
