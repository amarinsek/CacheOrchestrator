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
        await InvalidateAsync(eventData.Context, cancellationToken).ConfigureAwait(false);
        return await base.SavedChangesAsync(eventData, result, cancellationToken).ConfigureAwait(false);
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

        Dictionary<(string Domain, string EntityKind), List<string>> groups = [];
        foreach (PendingChange change in pending)
        {
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

            (string Domain, string EntityKind) key = (change.Mapping.Domain, change.Mapping.EntityKind);
            if (!groups.TryGetValue(key, out List<string>? ids))
            {
                ids = [];
                groups[key] = ids;
            }

            if (!ids.Contains(id, StringComparer.Ordinal))
                ids.Add(id);
        }

        EfCoreInvalidationOptions opts = _options.CurrentValue;
        foreach (KeyValuePair<(string Domain, string EntityKind), List<string>> group in groups)
        {
            try
            {
                await InvalidateGroupAsync(opts, group.Key.Domain, group.Key.EntityKind, group.Value, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
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
}
