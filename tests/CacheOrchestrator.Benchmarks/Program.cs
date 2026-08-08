using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Running;

// bin/Release/netX/ → 5× gor = root repota (benchmarks/Project/bin/Release/tfm)
var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

var artifacts = Path.Combine(repoRoot, "_local", "BenchmarkDotNet.Artifacts");
Directory.CreateDirectory(artifacts);

Console.WriteLine($"Artifacts path: {artifacts}");

var config = DefaultConfig.Instance.WithArtifactsPath(artifacts);

BenchmarkSwitcher
    .FromAssembly(typeof(Program).Assembly)
    .Run(args, config);