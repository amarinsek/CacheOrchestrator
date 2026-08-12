namespace CacheOrchestrator.Cluster;

/// <summary>
/// Marks the current async flow as applying a command that originated remotely
/// so local invalidation does not re-publish to the bus (anti-echo).
/// </summary>
public static class ClusterCommandScope
{
    private static readonly AsyncLocal<bool> Remote = new();

    /// <summary>True when the current flow is applying a remote (or distribute-receive) command.</summary>
    public static bool IsRemote => Remote.Value;

    /// <summary>
    /// Enters remote-apply scope until the returned handle is disposed.
    /// </summary>
    public static IDisposable EnterRemote()
    {
        bool previous = Remote.Value;
        Remote.Value = true;
        return new Reset(previous);
    }

    private sealed class Reset(bool previous) : IDisposable
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
}
