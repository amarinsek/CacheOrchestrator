namespace CacheOrchestrator.Cluster;

/// <summary>
/// Async flow flags for cluster apply / local-only Admin actions.
/// Suppresses bus re-publish on receive paths and optional local-only Admin ops.
/// </summary>
public static class ClusterCommandScope
{
    private static readonly AsyncLocal<bool> Remote = new();
    private static readonly AsyncLocal<bool> LocalOnly = new();

    /// <summary>True when applying a command that originated on another instance.</summary>
    public static bool IsRemote => Remote.Value;

    /// <summary>
    /// True when the current flow must not publish to the cluster bus
    /// (remote apply or explicit local-only Admin action).
    /// </summary>
    public static bool SuppressPublish => Remote.Value || LocalOnly.Value;

    /// <summary>
    /// Enters remote-apply scope (ApplyLocal only; no re-publish) until disposed.
    /// </summary>
    public static IDisposable EnterRemote()
    {
        bool previous = Remote.Value;
        Remote.Value = true;
        return new ResetRemote(previous);
    }

    /// <summary>
    /// Enters local-only scope (e.g. Admin <c>distribute: false</c>) until disposed.
    /// Invalidation still runs locally; bus publish is suppressed.
    /// </summary>
    public static IDisposable EnterLocalOnly()
    {
        bool previous = LocalOnly.Value;
        LocalOnly.Value = true;
        return new ResetLocalOnly(previous);
    }

    private sealed class ResetRemote(bool previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            Remote.Value = previous;
        }
    }

    private sealed class ResetLocalOnly(bool previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            LocalOnly.Value = previous;
        }
    }
}
