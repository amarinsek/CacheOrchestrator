using CacheOrchestrator.Admin;
using CacheOrchestrator.AdminConsole.Services.Hints.Declarative;

namespace CacheOrchestrator.AdminConsole.Services.Hints;

/// <summary>
/// Runs all registered hint rules and attaches results to domain/endpoint stats.
/// </summary>
public sealed class HintEngine
{
    private readonly HintRuleRegistry _registry;
    private readonly IHintRuleDisableStore _disable;
    private readonly TimeProvider _time;

    public HintEngine(HintRuleRegistry registry, IHintRuleDisableStore disable, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(disable);
        ArgumentNullException.ThrowIfNull(time);
        _registry = registry;
        _disable = disable;
        _time = time;
    }

    public IReadOnlyList<HintRuleCatalogEntry> GetCatalog()
    {
        List<HintRuleCatalogEntry> list = [];
        foreach (IHintRule rule in _registry.GetRules())
        {
            bool isCore = IsCoreSource(rule.Source);
            list.Add(new HintRuleCatalogEntry
            {
                Id = rule.Id + ":" + rule.Scope,
                Code = rule.Code,
                Badge = rule.Badge,
                Category = rule.Category,
                Scope = rule.Scope,
                Source = rule.Source,
                Description = rule.Description,
                DefaultSeverity = rule.DefaultSeverity,
                Enabled = !_disable.IsDisabled(rule.Code)
                    && (rule is not DeclarativeHintRule d || d.DefinitionEnabled),
                IsBuiltIn = isCore,
                EmittedCodes = rule.EmittedCodes,
                DefinitionJson = (rule as DeclarativeHintRule)?.DefinitionJson
            });
        }

        return list
            .OrderBy(e => e.IsBuiltIn ? 0 : 1)
            .ThenBy(e => e.Source, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.Code, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.Scope, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public AdminDomainStatsDto WithHints(
        AdminDomainStatsDto domain,
        AdminDomainConfigDto? config = null)
    {
        ArgumentNullException.ThrowIfNull(domain);
        IReadOnlyList<AdminHintDto> hints = EvaluateDomain(domain, config);
        return new AdminDomainStatsDto
        {
            Name = domain.Name,
            InstanceId = domain.InstanceId,
            Version = domain.Version,
            VersionIsRuntimeOverride = domain.VersionIsRuntimeOverride,
            SchedulePhase = domain.SchedulePhase,
            LastInvalidationUtc = domain.LastInvalidationUtc,
            Invalidations = domain.Invalidations,
            Requests = domain.Requests,
            PeakRequestRate = domain.PeakRequestRate,
            Oc = domain.Oc,
            Fc = domain.Fc,
            Pipeline = domain.Pipeline,
            Endpoints = domain.Endpoints.Select(WithHints).ToArray(),
            ByInstance = domain.ByInstance?
                .Select(b => WithHints(b, config))
                .ToArray(),
            InstanceSpread = domain.InstanceSpread,
            Impact = domain.Impact,
            Hints = hints
        };
    }

    public AdminEndpointStatsDto WithHints(AdminEndpointStatsDto ep)
    {
        ArgumentNullException.ThrowIfNull(ep);
        IReadOnlyList<AdminHintDto> hints = EvaluateEndpoint(ep);
        return new AdminEndpointStatsDto
        {
            Route = ep.Route,
            InstanceId = ep.InstanceId,
            ConfiguredDomain = ep.ConfiguredDomain,
            Requests = ep.Requests,
            PeakRequestRate = ep.PeakRequestRate,
            Oc = ep.Oc,
            Fc = ep.Fc,
            Pipeline = ep.Pipeline,
            ByInstance = ep.ByInstance?.Select(WithHints).ToArray(),
            InstanceSpread = ep.InstanceSpread,
            Impact = ep.Impact,
            Hints = hints
        };
    }

    public IReadOnlyList<AdminHintDto> EvaluateDomain(
        AdminDomainStatsDto domain,
        AdminDomainConfigDto? config)
    {
        var ctx = new HintEvaluationContext
        {
            NowUtc = _time.GetUtcNow(),
            Domain = domain,
            Config = config,
            ConfigByName = config is null
                ? new Dictionary<string, AdminDomainConfigDto>(StringComparer.Ordinal)
                : new Dictionary<string, AdminDomainConfigDto>(StringComparer.Ordinal)
                {
                    [config.Name] = config
                }
        };
        return Run(ctx, preferScope: "domain");
    }

    public IReadOnlyList<AdminHintDto> EvaluateEndpoint(AdminEndpointStatsDto ep)
    {
        var ctx = new HintEvaluationContext
        {
            NowUtc = _time.GetUtcNow(),
            Endpoint = ep
        };
        return Run(ctx, preferScope: "endpoint");
    }

    public static AdminHintSummaryDto Summarize(IEnumerable<AdminHintDto> hints)
    {
        int info = 0, warning = 0, critical = 0;
        foreach (AdminHintDto h in hints)
        {
            switch (h.Severity)
            {
                case "Critical":
                    critical++;
                    break;
                case "Warning":
                    warning++;
                    break;
                default:
                    info++;
                    break;
            }
        }

        return new AdminHintSummaryDto
        {
            Info = info,
            Warning = warning,
            Critical = critical
        };
    }

    public static IReadOnlyList<AdminHintDto> CollectFromStats(
        IReadOnlyList<AdminDomainStatsDto> domains,
        IReadOnlyList<AdminEndpointStatsDto>? endpoints = null)
    {
        List<AdminHintDto> list = [];
        HashSet<string> seen = new(StringComparer.Ordinal);

        void addRange(IEnumerable<AdminHintDto>? hints)
        {
            if (hints is null)
                return;
            foreach (AdminHintDto h in hints)
            {
                string key = h.Severity + "|" + h.Code + "|" + h.Message;
                if (seen.Add(key))
                    list.Add(h);
            }
        }

        foreach (AdminDomainStatsDto d in domains)
        {
            addRange(d.Hints);
            foreach (AdminEndpointStatsDto e in d.Endpoints)
                addRange(e.Hints);
        }

        if (endpoints is not null)
        {
            foreach (AdminEndpointStatsDto e in endpoints)
                addRange(e.Hints);
        }

        return list;
    }

    private IReadOnlyList<AdminHintDto> Run(HintEvaluationContext ctx, string preferScope)
    {
        List<AdminHintDto> list = [];
        HashSet<string> seen = new(StringComparer.Ordinal);

        foreach (IHintRule rule in _registry.GetRules())
        {
            string scope = rule.Scope.ToLowerInvariant();
            if (scope is not "any" && !string.Equals(scope, preferScope, StringComparison.Ordinal))
                continue;

            if (_disable.IsDisabled(rule.Code))
                continue;

            foreach (AdminHintDto h in rule.Evaluate(ctx))
            {
                if (_disable.IsDisabled(h.Code))
                    continue;
                string key = h.Severity + "|" + h.Code + "|" + h.Message;
                if (seen.Add(key))
                    list.Add(h);
            }
        }

        return list;
    }

    private static bool IsCoreSource(string source) =>
        source.Contains("core-hints.json", StringComparison.OrdinalIgnoreCase)
        || string.Equals(source, "built-in", StringComparison.OrdinalIgnoreCase);
}
