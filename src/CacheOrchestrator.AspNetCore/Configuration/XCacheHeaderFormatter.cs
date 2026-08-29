using System.Globalization;

namespace CacheOrchestrator.Configuration;

/// <summary>Builds the diagnostic <c>X-Cache</c> response header value.</summary>
public static class XCacheHeaderFormatter
{
    private const string PDomain = "domain=";
    private const string PVersion = "; version=";
    private const string PClient = "; client=";
    private const string PPhase = "; phase=";
    private const string POc = "; oc=";
    private const string PDc = "; dc=";
    private const string PFa = "; fa=";
    private const string FaRun = "run";
    private const string PMs = "; ms=";

    /// <summary>Formats the coordinated HTTP cache result for one request.</summary>
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
        bool includeDc = output != OutputCacheResult.Hit;
        string? dcStr = includeDc
            ? data is null ? "n/a" : DataToString(data.Value)
            : null;
        bool includeFa = includeDc && data != DataCacheResult.Hit;
        string? msStr = ms is not null && output != OutputCacheResult.Hit
            ? ms.Value.ToString(CultureInfo.InvariantCulture)
            : null;

        int length = PDomain.Length + domain.Length
            + PVersion.Length + version.Length
            + PClient.Length + clientStr.Length
            + PPhase.Length + phaseStr.Length
            + POc.Length + ocStr.Length;

        if (dcStr is not null)
            length += PDc.Length + dcStr.Length;
        if (includeFa)
            length += PFa.Length + FaRun.Length;
        if (msStr is not null)
            length += PMs.Length + msStr.Length;

        return string.Create(
            length,
            (domain, version, clientStr, phaseStr, ocStr, dcStr, includeFa, msStr),
            static (span, state) =>
            {
                int index = 0;
                index = Write(span, index, PDomain);
                index = Write(span, index, state.domain);
                index = Write(span, index, PVersion);
                index = Write(span, index, state.version);
                index = Write(span, index, PClient);
                index = Write(span, index, state.clientStr);
                index = Write(span, index, PPhase);
                index = Write(span, index, state.phaseStr);
                index = Write(span, index, POc);
                index = Write(span, index, state.ocStr);

                if (state.dcStr is not null)
                {
                    index = Write(span, index, PDc);
                    index = Write(span, index, state.dcStr);
                }

                if (state.includeFa)
                {
                    index = Write(span, index, PFa);
                    index = Write(span, index, FaRun);
                }

                if (state.msStr is not null)
                {
                    index = Write(span, index, PMs);
                    Write(span, index, state.msStr);
                }
            });
    }

    /// <summary>Returns the stable wire value for a Client Cache Schedule phase.</summary>
    public static string PhaseToString(ClientCacheSchedulePhase phase) => phase switch
    {
        ClientCacheSchedulePhase.Calm => "calm",
        ClientCacheSchedulePhase.Approaching => "approaching",
        ClientCacheSchedulePhase.Hold => "hold",
        ClientCacheSchedulePhase.NotApplicable => "n/a",
        _ => "n/a"
    };

    private static int Write(Span<char> span, int index, string value)
    {
        value.AsSpan().CopyTo(span[index..]);
        return index + value.Length;
    }

    private static string ClientToString(ClientCacheClass value) => value switch
    {
        ClientCacheClass.Private => "private",
        ClientCacheClass.NoStore => "no-store",
        ClientCacheClass.Blocked => "blocked",
        ClientCacheClass.Public => "public",
        _ => "public"
    };

    private static string OutputToString(OutputCacheResult value) => value switch
    {
        OutputCacheResult.Hit => "hit",
        OutputCacheResult.Bypass => "bypass",
        OutputCacheResult.Off => "off",
        OutputCacheResult.Miss => "miss",
        _ => "miss"
    };

    private static string DataToString(DataCacheResult value) => value switch
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
