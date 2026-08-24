using System.Globalization;

namespace CacheOrchestrator.Configuration;

/// <summary>
/// Builds the diagnostic <c>X-Cache</c> response header value.
/// </summary>
public static class XCacheHeaderFormatter
{
    // Fixed wire prefixes (ASCII) used by string.Create length math and copy.
    private const string PDomain = "domain=";
    private const string PVersion = "; version=";
    private const string PClient = "; client=";
    private const string PPhase = "; phase=";
    private const string POc = "; oc=";
    private const string PFc = "; fc=";
    private const string PFa = "; fa=";
    private const string FaRun = "run";
    private const string PMs = "; ms=";

    /// <summary>
    /// Formats domain, client class, schedule phase, OC/FC results, optional factory-run flag, and elapsed ms.
    /// </summary>
    /// <param name="domain">Normalized domain name.</param>
    /// <param name="client">Client cache class applied to the response.</param>
    /// <param name="output">Output Cache result (<c>oc=</c>).</param>
    /// <param name="data">Optional data-cache result (<c>fc=</c> wire token; omitted on OC HIT).</param>
    /// <param name="ms">Optional elapsed milliseconds (omitted on OC HIT).</param>
    /// <param name="version">Domain version token.</param>
    /// <param name="phase">Client Cache Schedule phase used for <c>Cache-Control</c>.</param>
    /// <returns>Header value string.</returns>
    /// <remarks>
    /// <c>fa=run</c> is written when <c>fc</c> is present and is not a fresh hit — the data-cache
    /// factory callback ran. OC HIT omits <c>fc</c> and <c>fa</c> (handler/factory did not run).
    /// </remarks>
    public static string Format(
        string domain,
        ClientCacheClass client,
        OutputCacheResult output,
        DataCacheResult? data,
        long? ms,
        string version,
        ClientCacheSchedulePhase phase = ClientCacheSchedulePhase.NotApplicable)
    {
        domain ??= string.Empty;
        version ??= string.Empty;

        string clientStr = ClientToString(client);
        string phaseStr = PhaseToString(phase);
        string ocStr = OutputToString(output);

        bool includeFc = output != OutputCacheResult.Hit && data is not null;
        string? fcStr = includeFc ? DataToString(data!.Value) : null;
        bool includeFa = includeFc && data!.Value != DataCacheResult.Hit;

        bool includeMs = ms is not null && output != OutputCacheResult.Hit;
        string? msStr = includeMs
            ? ms!.Value.ToString(CultureInfo.InvariantCulture)
            : null;

        int length =
            PDomain.Length + domain.Length
            + PVersion.Length + version.Length
            + PClient.Length + clientStr.Length
            + PPhase.Length + phaseStr.Length
            + POc.Length + ocStr.Length;

        if (includeFc)
            length += PFc.Length + fcStr!.Length;
        if (includeFa)
            length += PFa.Length + FaRun.Length;
        if (includeMs)
            length += PMs.Length + msStr!.Length;

        // Single allocation via string.Create (no StringBuilder intermediate buffer).
        return string.Create(
            length,
            (domain, version, clientStr, phaseStr, ocStr, fcStr, includeFa, msStr),
            static (span, s) =>
            {
                int i = 0;
                i = Write(span, i, PDomain);
                i = Write(span, i, s.domain);
                i = Write(span, i, PVersion);
                i = Write(span, i, s.version);
                i = Write(span, i, PClient);
                i = Write(span, i, s.clientStr);
                i = Write(span, i, PPhase);
                i = Write(span, i, s.phaseStr);
                i = Write(span, i, POc);
                i = Write(span, i, s.ocStr);

                if (s.fcStr is not null)
                {
                    i = Write(span, i, PFc);
                    i = Write(span, i, s.fcStr);
                }

                if (s.includeFa)
                {
                    i = Write(span, i, PFa);
                    i = Write(span, i, FaRun);
                }

                if (s.msStr is not null)
                {
                    i = Write(span, i, PMs);
                    Write(span, i, s.msStr);
                }
            });
    }

    private static int Write(Span<char> span, int index, string value)
    {
        value.AsSpan().CopyTo(span[index..]);
        return index + value.Length;
    }

    /// <summary>Wire format for <see cref="ClientCacheSchedulePhase"/> in X-Cache and metrics tags.</summary>
    public static string PhaseToString(ClientCacheSchedulePhase phase) => phase switch
    {
        ClientCacheSchedulePhase.Calm => "calm",
        ClientCacheSchedulePhase.Approaching => "approaching",
        ClientCacheSchedulePhase.Hold => "hold",
        ClientCacheSchedulePhase.NotApplicable => "n/a",
        _ => "n/a"
    };

    private static string ClientToString(ClientCacheClass c) => c switch
    {
        ClientCacheClass.Private => "private",
        ClientCacheClass.NoStore => "no-store",
        ClientCacheClass.Blocked => "blocked",
        ClientCacheClass.Public => "public",
        _ => "public"
    };

    private static string OutputToString(OutputCacheResult o) => o switch
    {
        OutputCacheResult.Hit => "hit",
        OutputCacheResult.Bypass => "bypass",
        OutputCacheResult.Off => "off",
        OutputCacheResult.Miss => "miss",
        _ => "miss"
    };

    private static string DataToString(DataCacheResult d) => d switch
    {
        DataCacheResult.Hit => "hit",
        DataCacheResult.Stale => "stale",
        DataCacheResult.Bypass => "bypass",
        DataCacheResult.Off => "off",
        DataCacheResult.Unresolved => "unresolved",
        DataCacheResult.Miss => "miss",
        _ => "miss"
    };
}