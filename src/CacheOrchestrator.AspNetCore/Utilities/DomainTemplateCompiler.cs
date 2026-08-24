using Microsoft.AspNetCore.Http;
using System.Buffers;
using System.Collections.Concurrent;
using System.Text;

namespace CacheOrchestrator.Utilities;

/// <summary>
/// Compiles lightweight "domain template" strings into fast resolvers that build
/// strings from an <see cref="HttpContext"/> at runtime.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Domain template syntax</strong> (mini-DSL, single-pass parser; no regex):
/// <list type="bullet">
///   <item><c>{host}</c> – request host without the port</item>
///   <item><c>{route:name}</c> – route value with key <c>name</c></item>
///   <item><c>{header:X-Name}</c> – HTTP header value by name</item>
///   <item><c>{query:p}</c> – query parameter value by key</item>
///   <item><c>{custom:key}</c> – custom provider value (see <see cref="GetOrAdd"/>)</item>
///   <item>Anything else is treated as a literal</item>
/// </list>
/// </para>
/// </remarks>
public static class DomainTemplateCompiler
{
    // Parsed plans are always shared per template string (providers do not affect parsing).
    private static readonly ConcurrentDictionary<string, Plan> _plans = new();

    // Compiled resolvers without custom providers — keyed by template only.
    // Resolvers that capture customProviders must NOT use this cache (would ignore later providers).
    private static readonly ConcurrentDictionary<string, Func<HttpContext, string>> _cache = new();

    /// <summary>
    /// Returns a compiled resolver delegate for the specified <paramref name="template"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Templates without <paramref name="customProviders"/> are cached per template string.
    /// When custom providers are supplied, the parse plan is still shared, but the resolver is
    /// built for that provider map and is <strong>not</strong> stored in the shared template
    /// cache (providers are part of the resolver identity).
    /// </para>
    /// </remarks>
    /// <param name="template">Domain template string (see class remarks for syntax).</param>
    /// <param name="customProviders">
    /// Optional map of custom key → provider.
    /// Used by <c>{custom:key}</c> segments (and unknown tokens).
    /// </param>
    public static Func<HttpContext, string> GetOrAdd(
        string template,
        IReadOnlyDictionary<string, Func<HttpContext, string?>>? customProviders = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(template);

        Plan plan = _plans.GetOrAdd(template, static t => Parse(t));

        // Shared cache only when there are no custom providers — otherwise the first compile
        // would pin the wrong provider map for all subsequent GetOrAdd calls with the same template.
        if (customProviders is null || customProviders.Count == 0)
        {
            return _cache.GetOrAdd(template, _ => Build(plan, EmptyCustom));
        }

        return Build(plan, customProviders);
    }

    // ---------------- Parsing ----------------

    private enum SegKind : byte
    {
        Literal,
        Host,
        Route,
        Header,
        Query,
        Custom
    }

    private readonly struct Segment
    {
        public readonly SegKind Kind;
        public readonly string Text; // Literal text or key (e.g., "tileset", "X-Api-Key", "foo")

        public Segment(SegKind kind, string text)
        {
            Kind = kind;
            Text = text;
        }
    }

    private sealed class Plan
    {
        public readonly List<Segment> Segments = new(8);

        public bool HasOnlyLiteral => Segments.TrueForAll(s => s.Kind == SegKind.Literal);
    }

    /// <summary>
    /// Parses a template string into a plan of segments. This is a single-pass, allocation-light parser.
    /// </summary>
    private static Plan Parse(string template)
    {
        Plan plan = new();
        ReadOnlySpan<char> src = template.AsSpan();
        int i = 0;
        int len = src.Length;

        StringBuilder lit = new();

        void FlushLiteral()
        {
            if (lit.Length > 0)
            {
                plan.Segments.Add(new Segment(SegKind.Literal, lit.ToString()));
                lit.Clear();
            }
        }

        while (i < len)
        {
            char ch = src[i];
            if (ch != '{')
            {
                lit.Append(ch);
                i++;
                continue;
            }

            // Find closing brace
            int end = template.IndexOf('}', i + 1);
            if (end < 0)
            {
                throw new FormatException($"Unclosed '{{' at position {i} in template '{template}'.");
            }

            // Token between '{' and '}'
            string token = template[(i + 1)..end].Trim();
            i = end + 1;

            // Flush previously collected literal
            FlushLiteral();

            if (token.Length == 0)
            {
                continue;
            }

            // Tokens: host | route:name | header:X | query:p | custom:key
            if (token.Equals("host", StringComparison.OrdinalIgnoreCase))
            {
                plan.Segments.Add(new Segment(SegKind.Host, string.Empty));
                continue;
            }

            int colon = token.IndexOf(':');
            if (colon < 0)
            {
                // Unknown identifier -> interpret as {custom:token}
                plan.Segments.Add(new Segment(SegKind.Custom, token));
                continue;
            }

            string kind = token[..colon].Trim().ToLowerInvariant();
            string key = token[(colon + 1)..].Trim();

            if (key.Length == 0)
            {
                throw new FormatException($"Empty key in token '{{{token}}}'.");
            }

            switch (kind)
            {
                case "route":
                    plan.Segments.Add(new Segment(SegKind.Route, key));
                    break;

                case "header":
                    plan.Segments.Add(new Segment(SegKind.Header, key));
                    break;

                case "query":
                    plan.Segments.Add(new Segment(SegKind.Query, key));
                    break;

                case "custom":
                    plan.Segments.Add(new Segment(SegKind.Custom, key));
                    break;

                default:
                    // Fallback to custom
                    plan.Segments.Add(new Segment(SegKind.Custom, token));
                    break;
            }
        }

        FlushLiteral();
        return plan;
    }

    // ---------------- Build / runtime "codegen" ----------------

    /// <summary>
    /// Builds an efficient resolver delegate for the parsed <paramref name="plan"/>.
    /// </summary>
    private static Func<HttpContext, string> Build(
        Plan plan,
        IReadOnlyDictionary<string, Func<HttpContext, string?>>? customProviders)
    {
        // Optimization: if the template is a pure literal, return a constant function (no pooled SB).
        if (plan.HasOnlyLiteral)
        {
            string s = string.Concat(plan.Segments.Select(s => s.Text));
            return _ => s;
        }

        // Pooled StringBuilder via an object pool.
        StringBuilderPool pool = StringBuilderPool.Shared;

        // Resolve custom providers to a dictionary (if provided).
        IReadOnlyDictionary<string, Func<HttpContext, string?>> custom = customProviders ?? EmptyCustom;

        return http =>
        {
            StringBuilder sb = pool.Get();
            try
            {
                foreach (Segment seg in plan.Segments)
                {
                    switch (seg.Kind)
                    {
                        case SegKind.Literal:
                            sb.Append(seg.Text);
                            break;

                        case SegKind.Host:
                            // Host without port; for host:port use http.Request.Host.Value
                            string host = http.Request.Host.Host;
                            if (!string.IsNullOrEmpty(host))
                            {
                                sb.Append(host);
                            }
                            break;

                        case SegKind.Route:
                            if (http.Request.RouteValues.TryGetValue(seg.Text, out object? rv) && rv is not null)
                            {
                                sb.Append(rv.ToString());
                            }
                            break;

                        case SegKind.Header:
                            Microsoft.Extensions.Primitives.StringValues hv = http.Request.Headers[seg.Text];
                            if (!StringValuesIsNullOrEmpty(hv))
                            {
                                sb.Append(hv.ToString());
                            }
                            break;

                        case SegKind.Query:
                            Microsoft.Extensions.Primitives.StringValues qv = http.Request.Query[seg.Text];
                            if (!StringValuesIsNullOrEmpty(qv))
                            {
                                sb.Append(qv.ToString());
                            }
                            break;

                        case SegKind.Custom:
                            if (custom.TryGetValue(seg.Text, out Func<HttpContext, string?>? prov))
                            {
                                string? val = prov(http);
                                if (!string.IsNullOrEmpty(val))
                                {
                                    sb.Append(val);
                                }
                            }
                            // Unknown custom key: append nothing (by design).
                            break;
                        default:
                            break;
                    }
                }

                // Single allocation for the final result
                return sb.ToString();
            }
            finally
            {
                pool.Return(sb);
            }
        };
    }

    /// <summary>
    /// Checks whether <see cref="Microsoft.Extensions.Primitives.StringValues"/> is null or empty.
    /// </summary>
    private static bool StringValuesIsNullOrEmpty(Microsoft.Extensions.Primitives.StringValues v) =>
        v.Count == 0 || (v.Count == 1 && string.IsNullOrEmpty(v[0]));

    private static readonly IReadOnlyDictionary<string, Func<HttpContext, string?>> EmptyCustom
        = new Dictionary<string, Func<HttpContext, string?>>();
}

/// <summary>
/// Minimal, fast <see cref="StringBuilder"/> pool backed by <see cref="ArrayPool{T}"/>.
/// Intended to reduce allocations in hot paths.
/// </summary>
internal sealed class StringBuilderPool
{
    /// <summary>
    /// Shared instance tuned for typical web workloads.
    /// </summary>
    public static readonly StringBuilderPool Shared =
        new(initialCapacity: 64, maxCapacity: 4096, poolSize: Environment.ProcessorCount * 2);

    private readonly ArrayPool<char> _charPool = ArrayPool<char>.Shared;
    private readonly ConcurrentBag<StringBuilder> _bag = [];
    private readonly int _initialCapacity;
    private readonly int _maxCapacity;
    private readonly int _poolSize;

    /// <summary>
    /// Creates a new pool.
    /// </summary>
    public StringBuilderPool(int initialCapacity, int maxCapacity, int poolSize)
    {
        _initialCapacity = initialCapacity;
        _maxCapacity = maxCapacity;
        _poolSize = Math.Max(4, poolSize);
    }

    /// <summary>
    /// Gets a <see cref="StringBuilder"/> from the pool.
    /// </summary>
    public StringBuilder Get()
    {
        if (_bag.TryTake(out StringBuilder? sb))
        {
            return sb;
        }

        // New StringBuilder (benefits from .NET internal pooling of its char[] via ArrayPool)
        return new StringBuilder(_initialCapacity);
    }

    /// <summary>
    /// Returns a <see cref="StringBuilder"/> to the pool. Builders above <c>_maxCapacity</c> are dropped.
    /// </summary>
    public void Return(StringBuilder sb)
    {
        if (sb.Capacity > _maxCapacity)
        {
            // Too large – let GC collect it.
            return;
        }

        sb.Clear();
        if (_bag.Count < _poolSize)
        {
            _bag.Add(sb);
        }
    }
}