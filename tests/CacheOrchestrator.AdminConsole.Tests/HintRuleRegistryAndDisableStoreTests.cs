using CacheOrchestrator.Admin;
using CacheOrchestrator.AdminConsole.Options;
using CacheOrchestrator.AdminConsole.Services.Hints;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CacheOrchestrator.AdminConsole.Tests;

public class HintRuleRegistryAndDisableStoreTests
{
    [Fact]
    public void Registry_LoadsCorePack_AndReloadKeepsRules()
    {
        using TestHintHost host = TestHintHost.Create();
        HintRuleLoadStatus status = host.Registry.GetLoadStatus();
        status.Ok.Should().BeTrue();
        status.RuleCount.Should().BeGreaterThan(0);
        status.FileCount.Should().BeGreaterThan(0);

        HintRuleLoadStatus reloaded = host.Registry.Reload();
        reloaded.Ok.Should().BeTrue();
        reloaded.RuleCount.Should().Be(status.RuleCount);
        host.Engine.GetCatalog().Should().Contain(e => e.Code == "high-factory-share");
    }

    [Fact]
    public async Task DisableStore_PersistsAndAffectsEngineCatalog()
    {
        using TestHintHost host = TestHintHost.Create();
        host.Engine.GetCatalog()
            .Should().Contain(e => e.Code == "high-factory-share" && e.Enabled);

        await host.Disable.SetEnabledAsync("high-factory-share", enabled: false, TestContext.Current.CancellationToken);
        host.Disable.IsDisabled("high-factory-share").Should().BeTrue();
        host.Engine.GetCatalog()
            .Where(e => e.Code == "high-factory-share")
            .Should().OnlyContain(e => !e.Enabled);

        string path = Path.Combine(host.ContentRoot, "hints", "disabled.local.json");
        File.Exists(path).Should().BeTrue();
        File.ReadAllText(path).Should().Contain("high-factory-share");

        // New store instance reloads persisted disables.
        HintRuleDisableStore reloaded = new(host.Monitor, host.Env);
        reloaded.IsDisabled("high-factory-share").Should().BeTrue();
    }

    [Fact]
    public async Task DisableStore_DisabledCode_DoesNotEmitHints()
    {
        using TestHintHost host = TestHintHost.Create();
        await host.Disable.SetEnabledAsync("critical-factory-share", enabled: false, TestContext.Current.CancellationToken);

        (_, AdminLayerDto oc, AdminFusionLayerDto fc, AdminPipelineDto pipe) =
            AdminStatsMath.BuildAll(
                ocHits: 10, ocMisses: 40, ocBypass: 0,
                fcHits: 5, fcMisses: 35, fcStale: 0, fcBypass: 0,
                factoryRuns: 35, factoryFailures: 0);

        IReadOnlyList<AdminHintDto> hints = host.Engine.EvaluateDomain(
            new AdminDomainStatsDto
            {
                Name = "hot",
                Version = "1",
                Requests = 50,
                Oc = oc,
                Fc = fc,
                Pipeline = pipe,
            },
            config: null);

        hints.Should().NotContain(h => h.Code == "critical-factory-share");
    }

    private sealed class TestHintHost : IDisposable
    {
        public required string ContentRoot { get; init; }
        public required TestOptionsMonitor<AdminConsoleOptions> Monitor { get; init; }
        public required TestHostEnvironment Env { get; init; }
        public required HintRuleRegistry Registry { get; init; }
        public required HintRuleDisableStore Disable { get; init; }
        public required HintEngine Engine { get; init; }

        public static TestHintHost Create()
        {
            string root = Path.Combine(Path.GetTempPath(), "co-hint-reg-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(root, "hints"));
            string? core = FindCoreHintsPath();
            core.Should().NotBeNull("core-hints.json must be discoverable from the test host");
            File.Copy(core!, Path.Combine(root, "hints", "core-hints.json"), overwrite: true);

            AdminConsoleOptions opts = new()
            {
                Hints = new HintOptions
                {
                    DisabledStatePath = "hints/disabled.local.json",
                    RuleFiles = [],
                },
            };
            TestOptionsMonitor<AdminConsoleOptions> monitor = new(opts);
            TestHostEnvironment env = new(root);
            HintRuleRegistry registry = new(monitor, env, NullLogger<HintRuleRegistry>.Instance);
            HintRuleDisableStore disable = new(monitor, env);
            HintEngine engine = new(registry, disable, TimeProvider.System);
            return new TestHintHost
            {
                ContentRoot = root,
                Monitor = monitor,
                Env = env,
                Registry = registry,
                Disable = disable,
                Engine = engine,
            };
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(ContentRoot))
                    Directory.Delete(ContentRoot, recursive: true);
            }
            catch
            {
                // best-effort temp cleanup
            }
        }

        private static string? FindCoreHintsPath()
        {
            string? dir = AppContext.BaseDirectory;
            for (int i = 0; i < 8 && dir is not null; i++)
            {
                string candidate = Path.Combine(dir, "src", "CacheOrchestrator.AdminConsole", "hints", "core-hints.json");
                if (File.Exists(candidate))
                    return candidate;
                dir = Directory.GetParent(dir)?.FullName;
            }

            string fromCwd = Path.GetFullPath(Path.Combine(
                Directory.GetCurrentDirectory(),
                "src", "CacheOrchestrator.AdminConsole", "hints", "core-hints.json"));
            return File.Exists(fromCwd) ? fromCwd : null;
        }
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
