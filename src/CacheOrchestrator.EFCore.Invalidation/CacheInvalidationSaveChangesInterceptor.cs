using CacheOrchestrator.Invalidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Runtime.CompilerServices;

namespace CacheOrchestrator.EFCore;

/// <summary>
/// Snapshots mapped <c>Added</c>/<c>Modified</c>/<c>Deleted</c> entries in <c>SavingChanges</c>
/// and invalidates them after a successful save. Failures are logged and do not fail the save.
/// </summary>
/// <remarks>
/// Register as a singleton and attach per <c>DbContext</c> with
/// <c>DbContextOptionsBuilder.AddCacheOrchestratorInvalidation</c>.
/// Pending work is stored per context (not on this instance) so pooling is safe.
/// </remarks>
public sealed class CacheInvalidationSaveChangesInterceptor : SaveChangesInterceptor
{
    private static readonly ConditionalWeakTable<DbContext, List<PendingChange>> Pending = [];

    private readonly ICacheOrchestratorInvalidator _invalidator;
    private readonly IEntityCacheMappingResolver _resolver;
    private readonly IOptionsMonitor<EfCoreInvalidationOptions> _options;
    private readonly ILogger<CacheInvalidationSaveChangesInterceptor> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="CacheInvalidationSaveChangesInterceptor"/> class.
    /// </summary>
    internal CacheInvalidationSaveChangesInterceptor(
        ICacheOrchestratorInvalidator invalidator,
        IEntityCacheMappingResolver resolver,
        IOptionsMonitor<EfCoreInvalidationOptions> options,
        ILogger<CacheInvalidationSaveChangesInterceptor> logger)
    {
        ArgumentNullException.ThrowIfNull(invalidator);
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _invalidator = invalidator;
        _resolver = resolver;
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc />
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Capture(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Capture(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    /// <inheritdoc />
    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        InvalidateAsync(eventData.Context, CancellationToken.None).AsTask().GetAwaiter().GetResult();
        return base.SavedChanges(eventData, result);
    }

    /// <inheritdoc />
    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        // The database write has already completed. Caller cancellation must not turn a committed
        // save into a canceled result or skip post-commit invalidation.
        await InvalidateAsync(eventData.Context, CancellationToken.None).ConfigureAwait(false);
        return await base.SavedChangesAsync(eventData, result, CancellationToken.None).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override void SaveChangesFailed(DbContextErrorEventData eventData)
    {
        Discard(eventData.Context);
        base.SaveChangesFailed(eventData);
    }

    /// <inheritdoc />
    public override Task SaveChangesFailedAsync(
        DbContextErrorEventData eventData,
        CancellationToken cancellationToken = default)
    {
        Discard(eventData.Context);
        return base.SaveChangesFailedAsync(eventData, cancellationToken);
    }

    private void Capture(DbContext? context)
    {
        if (context is null)
            return;

        Discard(context);

        if (!_options.CurrentValue.Enabled)
            return;

        List<PendingChange>? pending = null;
        foreach (EntityEntry entry in context.ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
                continue;

            if (entry.Metadata.IsOwned() || entry.Metadata.FindPrimaryKey() is null)
                continue;

            if (!_resolver.TryResolve(entry.Metadata, out EntityCacheMapping mapping))
                continue;

            string? capturedId = entry.State == EntityState.Deleted
                ? EntityResourceIdFormatter.TryFormat(entry)
                : null;

            pending ??= [];
            pending.Add(new PendingChange(entry, mapping, entry.State, capturedId));
        }

        if (pending is { Count: > 0 })
            Pending.Add(context, pending);
    }

    private async ValueTask InvalidateAsync(DbContext? context, CancellationToken cancellationToken)
    {
        if (context is null)
            return;

        if (!Pending.TryGetValue(context, out List<PendingChange>? pending))
            return;

        Discard(context);

        if (pending.Count == 0 || !_options.CurrentValue.Enabled)
            return;

        EfCoreInvalidationOptions opts = _options.CurrentValue;
        Dictionary<(string Domain, string EntityKind), PendingInvalidationGroup> groups = [];
        foreach (PendingChange change in pending)
        {
            (string Domain, string EntityKind) key = (change.Mapping.Domain, change.Mapping.EntityKind);
            groups.TryGetValue(key, out PendingInvalidationGroup? group);
            if (group?.BulkThresholdReached == true)
                continue;

            string? id = change.State == EntityState.Deleted
                ? change.ResourceIdAtCapture
                : EntityResourceIdFormatter.TryFormat(change.Entry);

            if (string.IsNullOrEmpty(id))
            {
                if (_logger.IsEnabled(LogLevel.Warning))
                {
                    _logger.LogWarning(
                        "Skipping cache invalidation for {ClrType}: primary key could not be formatted.",
                        change.Entry.Metadata.ClrType.Name);
                }

                continue;
            }

            if (group is null)
            {
                group = new PendingInvalidationGroup();
                groups[key] = group;
            }

            if (group.Add(id)
                && opts.OnBulk != EfCoreOnBulk.Entities
                && opts.BulkThreshold > 0
                && group.Ids.Count >= opts.BulkThreshold)
            {
                group.BulkThresholdReached = true;
            }
        }

        foreach (KeyValuePair<(string Domain, string EntityKind), PendingInvalidationGroup> group in groups)
        {
            try
            {
                await InvalidateGroupAsync(
                        opts,
                        group.Key.Domain,
                        group.Key.EntityKind,
                        group.Value.Ids,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Cache invalidation failed after SaveChanges for domain '{Domain}' entityKind '{EntityKind}'.",
                    group.Key.Domain,
                    group.Key.EntityKind);
            }
        }
    }

    private async ValueTask InvalidateGroupAsync(
        EfCoreInvalidationOptions opts,
        string domain,
        string entityKind,
        List<string> ids,
        CancellationToken cancellationToken)
    {
        bool bulk = opts.OnBulk != EfCoreOnBulk.Entities
            && opts.BulkThreshold > 0
            && ids.Count >= opts.BulkThreshold;

        if (bulk && opts.OnBulk == EfCoreOnBulk.Domain)
        {
            await _invalidator.InvalidateDomainAsync(domain, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (bulk && opts.OnBulk == EfCoreOnBulk.Kind)
        {
            await _invalidator.InvalidateEntityKindAsync(domain, entityKind, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        await _invalidator.InvalidateEntitiesAsync(domain, entityKind, ids, cancellationToken)
            .ConfigureAwait(false);
    }

    private static void Discard(DbContext? context)
    {
        if (context is null)
            return;

        Pending.Remove(context);
    }

    private sealed class PendingChange(
        EntityEntry entry,
        EntityCacheMapping mapping,
        EntityState state,
        string? resourceIdAtCapture)
    {
        public EntityEntry Entry { get; } = entry;
        public EntityCacheMapping Mapping { get; } = mapping;
        public EntityState State { get; } = state;
        public string? ResourceIdAtCapture { get; } = resourceIdAtCapture;
    }

    private sealed class PendingInvalidationGroup
    {
        private const int HashSetThreshold = 4;
        private HashSet<string>? _seen;

        public List<string> Ids { get; } = [];
        public bool BulkThresholdReached { get; set; }

        public bool Add(string id)
        {
            if (_seen is not null)
            {
                if (!_seen.Add(id))
                    return false;
            }
            else
            {
                if (Ids.Contains(id, StringComparer.Ordinal))
                    return false;

                if (Ids.Count == HashSetThreshold)
                {
                    _seen = new HashSet<string>(Ids, StringComparer.Ordinal) { id };
                }
            }

            Ids.Add(id);
            return true;
        }
    }
}
