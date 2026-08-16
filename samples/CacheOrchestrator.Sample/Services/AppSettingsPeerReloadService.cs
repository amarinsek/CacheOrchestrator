namespace CacheOrchestrator.Sample.Services;

/// <summary>
/// Multi-instance labs share <c>appsettings.json</c> on a volume. The node that Saves calls
/// <see cref="IConfigurationRoot.Reload"/>; peers must notice the file change themselves.
/// FileSystemWatcher is unreliable on some volume mounts, so this sample polls mtime/size.
/// </summary>
public sealed class AppSettingsPeerReloadService : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<AppSettingsPeerReloadService> _logger;
    private DateTime _lastWriteUtc;
    private long _lastLength;
    private bool _initialized;

    public AppSettingsPeerReloadService(
        IConfiguration configuration,
        IWebHostEnvironment env,
        ILogger<AppSettingsPeerReloadService> logger)
    {
        _configuration = configuration;
        _env = env;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        string path = Path.Combine(_env.ContentRootPath, "appsettings.json");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(PollInterval, stoppingToken).ConfigureAwait(false);
                TryReloadIfChanged(path);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "appsettings.json poll failed.");
            }
        }
    }

    private void TryReloadIfChanged(string path)
    {
        if (!File.Exists(path))
            return;

        var info = new FileInfo(path);
        info.Refresh();
        DateTime writeUtc = info.LastWriteTimeUtc;
        long length = info.Length;

        if (!_initialized)
        {
            _lastWriteUtc = writeUtc;
            _lastLength = length;
            _initialized = true;
            return;
        }

        if (writeUtc == _lastWriteUtc && length == _lastLength)
            return;

        _lastWriteUtc = writeUtc;
        _lastLength = length;

        if (_configuration is not IConfigurationRoot root)
            return;

        root.Reload();
        _logger.LogInformation(
            "appsettings.json changed on disk; configuration reloaded (shared volume / peer save).");
    }
}
