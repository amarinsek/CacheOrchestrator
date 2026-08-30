using System.Diagnostics;

namespace CacheOrchestrator.Admin;

/// <summary>
/// Process lifetime anchors for Admin API health (uptime).
/// </summary>
internal static class AdminProcessInfo
{
    /// <summary>
    /// UTC start of the current host process (best-effort via <see cref="Process.StartTime"/>).
    /// </summary>
    public static DateTimeOffset StartedAtUtc { get; } = ResolveProcessStartUtc();

    private static DateTimeOffset ResolveProcessStartUtc()
    {
        try
        {
            DateTime start = Process.GetCurrentProcess().StartTime.ToUniversalTime();
            return new DateTimeOffset(start, TimeSpan.Zero);
        }
        catch
        {
            return DateTimeOffset.UtcNow;
        }
    }
}
