using CacheOrchestrator.Configuration;
using CacheOrchestrator.Invalidation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CacheOrchestrator.Cluster;

/// <summary>
/// Default local applicator: namespace/self checks, then invalidation under <see cref="ClusterCommandScope"/>.
/// </summary>
internal sealed class DefaultClusterCommandHandler : IClusterCommandHandler
{
    private readonly ICacheOrchestratorInvalidator _invalidator;
    private readonly IInstanceIdProvider _instanceId;
    private readonly IOptionsMonitor<CacheOrchestratorOptions> _options;
    private readonly ILogger<DefaultClusterCommandHandler> _logger;

    public DefaultClusterCommandHandler(
        ICacheOrchestratorInvalidator invalidator,
        IInstanceIdProvider instanceId,
        IOptionsMonitor<CacheOrchestratorOptions> options,
        ILogger<DefaultClusterCommandHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(invalidator);
        ArgumentNullException.ThrowIfNull(instanceId);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _invalidator = invalidator;
        _instanceId = instanceId;
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task ApplyLocalAsync(ClusterCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        string localNs = _options.CurrentValue.Namespace ?? string.Empty;
        if (!string.Equals(command.Namespace, localNs, StringComparison.Ordinal))
        {
            _logger.LogDebug(
                "Ignoring cluster command {CommandId}: namespace mismatch (command={CommandNs}, local={LocalNs})",
                command.CommandId,
                command.Namespace,
                localNs);
            return;
        }

        if (string.Equals(command.OriginInstanceId, _instanceId.InstanceId, StringComparison.Ordinal))
        {
            _logger.LogDebug(
                "Ignoring cluster command {CommandId}: origin is self ({InstanceId})",
                command.CommandId,
                _instanceId.InstanceId);
            return;
        }

        using (ClusterCommandScope.EnterRemote())
        {
            switch (command)
            {
                case InvalidateCommand inv:
                    await ApplyInvalidateAsync(inv, cancellationToken).ConfigureAwait(false);
                    break;

                default:
                    _logger.LogWarning(
                        "Unsupported cluster command type {Type} ({CommandId})",
                        command.GetType().Name,
                        command.CommandId);
                    break;
            }
        }
    }

    private async Task ApplyInvalidateAsync(InvalidateCommand command, CancellationToken cancellationToken)
    {
        // Prefer kind-specific APIs so Fusion instance routing matches origin behaviour.
        if (command.Kind == CacheInvalidationKind.Domain && !string.IsNullOrWhiteSpace(command.Domain))
        {
            await _invalidator.InvalidateDomainAsync(command.Domain, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (command.Kind == CacheInvalidationKind.Entity
            && !string.IsNullOrWhiteSpace(command.Domain)
            && !string.IsNullOrWhiteSpace(command.EntityId))
        {
            await _invalidator.InvalidateEntityAsync(command.Domain, command.EntityId, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        if (command.Tags is { Length: > 0 })
        {
            await _invalidator.InvalidateTagsAsync(command.Tags, cancellationToken).ConfigureAwait(false);
            return;
        }

        _logger.LogWarning(
            "InvalidateCommand {CommandId} had no domain/entity/tags to apply (scope={Scope})",
            command.CommandId,
            command.Scope);
    }
}
