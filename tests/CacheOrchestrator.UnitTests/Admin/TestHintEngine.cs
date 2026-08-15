using CacheOrchestrator.Admin.App.Options;
using CacheOrchestrator.Admin.App.Services.Hints;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CacheOrchestrator.UnitTests.Admin;

internal static class TestHintEngine
{
    public static HintEngine Create(CacheAdminOptions? opts = null)
    {
        opts ??= new CacheAdminOptions();
        TestOptionsMonitor<CacheAdminOptions> monitor = new(opts);
        string root = Path.Combine(Path.GetTempPath(), "co-admin-hints-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "hints"));
        CopyCoreHints(root);
        TestHostEnvironment env = new(root);
        HintRuleRegistry registry = new(monitor, env, NullLogger<HintRuleRegistry>.Instance);
        HintRuleDisableStore disable = new(monitor, env);
        return new HintEngine(registry, disable, TimeProvider.System);
    }

    private static void CopyCoreHints(string contentRoot)
    {
        string? src = FindCoreHintsPath();
        if (src is null)
            return;
        string dest = Path.Combine(contentRoot, "hints", "core-hints.json");
        File.Copy(src, dest, overwrite: true);
    }

    private static string? FindCoreHintsPath()
    {
        string? dir = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && dir is not null; i++)
        {
            string candidate = Path.Combine(dir, "src", "CacheOrchestrator.Admin", "hints", "core-hints.json");
            if (File.Exists(candidate))
                return candidate;
            dir = Directory.GetParent(dir)?.FullName;
        }

        // Running from repo root workspace
        string fromCwd = Path.GetFullPath(Path.Combine(
            Directory.GetCurrentDirectory(),
            "src", "CacheOrchestrator.Admin", "hints", "core-hints.json"));
        return File.Exists(fromCwd) ? fromCwd : null;
    }

    private sealed class TestOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue { get; } = value;
        public T Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    private sealed class TestHostEnvironment(string contentRoot) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "tests";
        public string ContentRootPath { get; set; } = contentRoot;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
