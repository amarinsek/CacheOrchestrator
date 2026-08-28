using Microsoft.Extensions.Options;

namespace CacheOrchestrator.HttpBus;

internal sealed class HttpBusOptionsValidator : IValidateOptions<HttpBusOptions>
{
    public ValidateOptionsResult Validate(string? name, HttpBusOptions options)
    {
        HttpBusTransportOptions bus = options.Cluster.Bus;
        if (!bus.Enabled)
            return ValidateOptionsResult.Success;

        List<string> failures = [];
        if (string.IsNullOrEmpty(HttpClusterCommandBus.ResolveApiKey(options))
            && !bus.AllowUnauthenticated)
        {
            failures.Add(
                "Cache:Cluster:Bus requires ApiKey (or Admin:ApiKey fallback) when enabled. " +
                "Set AllowUnauthenticated=true only for an explicitly isolated development network.");
        }

        if (bus.CommandMaxAgeSeconds is <= 0 or > 86400)
            failures.Add("Cache:Cluster:Bus:CommandMaxAgeSeconds must be in the range 1-86400.");
        if (bus.ClockSkewSeconds is < 0 or > 3600)
            failures.Add("Cache:Cluster:Bus:ClockSkewSeconds must be in the range 0-3600.");
        if (bus.DedupeWindowSeconds < bus.CommandMaxAgeSeconds + bus.ClockSkewSeconds)
        {
            failures.Add(
                "Cache:Cluster:Bus:DedupeWindowSeconds must be >= " +
                "CommandMaxAgeSeconds + ClockSkewSeconds so a command cannot be replayed while its timestamp is valid.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
