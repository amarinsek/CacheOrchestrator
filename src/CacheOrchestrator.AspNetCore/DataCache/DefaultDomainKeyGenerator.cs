using CacheOrchestrator.Configuration;
using CacheOrchestrator.Vary;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;
using System.Buffers;
using System.IO.Hashing;
using System.Runtime.CompilerServices;
using System.Text;

namespace CacheOrchestrator.DataCache;

/// <summary>
/// Default high-performance key generator using XxHash3.
/// </summary>
public sealed class DefaultDomainKeyGenerator : IDomainKeyGenerator
{
    private static readonly byte[] PrefixRoute = "r:"u8.ToArray();
    private static readonly byte[] PrefixParam = "|p:"u8.ToArray();
    private static readonly byte[] PrefixPath = "path:"u8.ToArray();
    private static readonly byte[] PrefixQuery = "|q:"u8.ToArray();
    private static readonly byte[] PrefixEnc = "|e:"u8.ToArray();
    private static readonly byte[] PrefixHdr = "|hdr:"u8.ToArray();
    private static readonly byte[] PrefixVal = "|v:"u8.ToArray();
    private static readonly byte[] PrefixScheme = "|s:"u8.ToArray();
    private static readonly byte[] PrefixHost = "|h:"u8.ToArray();
    private static readonly byte[] EqualsSign = "="u8.ToArray();
    private static readonly byte[] Comma = ","u8.ToArray();
    private static readonly byte[] Colon = ":"u8.ToArray();

    private readonly CacheVaryMaterializer _materializer;

    /// <summary>Creates a generator with no <see cref="ICacheVaryContributor"/> registrations.</summary>
    public DefaultDomainKeyGenerator()
        : this(new CacheVaryMaterializer())
    {
    }

    /// <summary>Creates a generator that uses the shared <paramref name="materializer"/>.</summary>
    public DefaultDomainKeyGenerator(CacheVaryMaterializer materializer)
    {
        ArgumentNullException.ThrowIfNull(materializer);
        _materializer = materializer;
    }

    /// <summary>Creates a generator that runs the given contributors after built-in vary rules.</summary>
    public DefaultDomainKeyGenerator(IEnumerable<ICacheVaryContributor> contributors)
        : this(new CacheVaryMaterializer(contributors))
    {
    }

    /// <inheritdoc />
    public string Generate(DomainCacheOptions opts, HttpContext http)
    {
        ArgumentNullException.ThrowIfNull(opts);
        ArgumentNullException.ThrowIfNull(http);

        XxHash3 hasher = new();

        Span<byte> byteBuffer = stackalloc byte[512];
        Span<char> charBuffer = stackalloc char[256];

        byte[]? rentedBytes = null;
        char[]? rentedChars = null;

        IHeaderDictionary headers = http.Request.Headers;
        bool hadAccept = headers.ContainsKey(HeaderNames.Accept);
        StringValues originalAccept = hadAccept ? headers.Accept : default;
        bool hadAcceptLanguage = headers.ContainsKey(HeaderNames.AcceptLanguage);
        StringValues originalAcceptLanguage = hadAcceptLanguage ? headers.AcceptLanguage : default;

        try
        {
            CacheVaryMaterial vary = _materializer.Build(http, opts, CacheVarySurface.Fusion);

            // 0. Entity identity (CRUD) — both kind and id are required; no id-only key shape.
            ICacheOrchestratorFeature? feature = http.Features.Get<ICacheOrchestratorFeature>();
            if (feature?.EntityKind is { Length: > 0 } entityKind
                && feature.ResourceId is { Length: > 0 } resourceId)
            {
                AppendRaw(hasher, "id:"u8);
                AppendString(hasher, entityKind, ref byteBuffer, ref rentedBytes, ref charBuffer, ref rentedChars, lowercase: false);
                AppendRaw(hasher, ":"u8);
                AppendString(hasher, resourceId, ref byteBuffer, ref rentedBytes, ref charBuffer, ref rentedChars, lowercase: false);

                AppendVaryMaterial(hasher, http, opts, vary, includeQuery: false, ref byteBuffer, ref rentedBytes, ref charBuffer, ref rentedChars);

                if (opts.DataCacheVaryOnPublicAddress)
                    AppendPublicAddress(hasher, http, ref byteBuffer, ref rentedBytes, ref charBuffer, ref rentedChars);

                ulong resourceHash = hasher.GetCurrentHashAsUInt64();
                return string.Create(
                    null,
                    stackalloc char[256],
                    $"{opts.Domain}:{opts.VersionHex}:id:{entityKind}:{resourceId}:{resourceHash:x16}");
            }

            // 1. Route / path
            if (http.GetEndpoint() is RouteEndpoint endpoint)
            {
                AppendRaw(hasher, PrefixRoute);
                AppendString(hasher, endpoint.RoutePattern.RawText, ref byteBuffer, ref rentedBytes, ref charBuffer, ref rentedChars, lowercase: true);

                foreach (RoutePatternParameterPart p in endpoint.RoutePattern.Parameters)
                {
                    AppendRaw(hasher, PrefixParam);

                    if (http.Request.RouteValues.TryGetValue(p.Name, out object? value) && value is not null)
                    {
                        if (value is string s)
                            AppendString(hasher, s, ref byteBuffer, ref rentedBytes, ref charBuffer, ref rentedChars, lowercase: true);
                        else
                            AppendString(hasher, value.ToString(), ref byteBuffer, ref rentedBytes, ref charBuffer, ref rentedChars, lowercase: true);
                    }
                }
            }
            else
            {
                AppendRaw(hasher, PrefixPath);
                AppendString(hasher, http.Request.Path.Value, ref byteBuffer, ref rentedBytes, ref charBuffer, ref rentedChars, lowercase: true);
            }

            // 2. Query + header/auth/custom vary (+ encoding via materializer)
            AppendVaryMaterial(hasher, http, opts, vary, includeQuery: true, ref byteBuffer, ref rentedBytes, ref charBuffer, ref rentedChars);

            // 3. Public address
            if (opts.DataCacheVaryOnPublicAddress)
                AppendPublicAddress(hasher, http, ref byteBuffer, ref rentedBytes, ref charBuffer, ref rentedChars);

            ulong hash = hasher.GetCurrentHashAsUInt64();
            return string.Create(null, stackalloc char[128], $"{opts.Domain}:{opts.VersionHex}:{hash:x16}");
        }
        finally
        {
            RestoreHeader(headers, HeaderNames.Accept, hadAccept, originalAccept);
            RestoreHeader(headers, HeaderNames.AcceptLanguage, hadAcceptLanguage, originalAcceptLanguage);

            if (rentedBytes != null)
                ArrayPool<byte>.Shared.Return(rentedBytes);
            if (rentedChars != null)
                ArrayPool<char>.Shared.Return(rentedChars);
        }
    }

    private static void RestoreHeader(
        IHeaderDictionary headers,
        string name,
        bool hadValue,
        StringValues original)
    {
        if (hadValue)
            headers[name] = original;
        else
            headers.Remove(name);
    }

    private static void AppendPublicAddress(
        XxHash3 hasher,
        HttpContext http,
        ref Span<byte> byteBuffer,
        ref byte[]? rentedBytes,
        ref Span<char> charBuffer,
        ref char[]? rentedChars)
    {
        AppendRaw(hasher, PrefixScheme);
        AppendString(hasher, http.Request.Scheme, ref byteBuffer, ref rentedBytes, ref charBuffer, ref rentedChars, lowercase: true);

        AppendRaw(hasher, PrefixHost);
        AppendString(hasher, http.Request.Host.Value, ref byteBuffer, ref rentedBytes, ref charBuffer, ref rentedChars, lowercase: true);
    }

    private static void AppendVaryMaterial(
        XxHash3 hasher,
        HttpContext http,
        DomainCacheOptions opts,
        CacheVaryMaterial vary,
        bool includeQuery,
        ref Span<byte> byteBuffer,
        ref byte[]? rentedBytes,
        ref Span<char> charBuffer,
        ref char[]? rentedChars)
    {
        if (includeQuery)
        {
            IReadOnlyList<string> queryKeys = vary.QueryKeys;
            if (queryKeys.Count > 0)
            {
                string[] keys = ArrayPool<string>.Shared.Rent(queryKeys.Count);
                try
                {
                    for (int i = 0; i < queryKeys.Count; i++)
                        keys[i] = queryKeys[i];

                    int keyCount = queryKeys.Count;
                    if (keyCount > 1)
                        Array.Sort(keys, 0, keyCount, StringComparer.OrdinalIgnoreCase);

                    IQueryCollection query = http.Request.Query;
                    for (int i = 0; i < keyCount; i++)
                    {
                        string key = keys[i];
                        AppendRaw(hasher, PrefixQuery);
                        AppendString(hasher, key, ref byteBuffer, ref rentedBytes, ref charBuffer, ref rentedChars, lowercase: true);
                        AppendRaw(hasher, EqualsSign);
                        AppendStringValues(hasher, query[key], ref byteBuffer, ref rentedBytes, ref charBuffer, ref rentedChars);
                    }
                }
                finally
                {
                    ArrayPool<string>.Shared.Return(keys);
                }
            }
        }

        // Header names (non-sensitive): hash current request values.
        // Accept-Encoding uses the historical "|e:" prefix so existing keys stay stable when only encoding varies.
        for (int i = 0; i < vary.HeaderNames.Count; i++)
        {
            string headerName = vary.HeaderNames[i];
            StringValues values = http.Request.Headers[headerName];
            if (values.Count == 0)
                continue;

            if (string.Equals(headerName, HeaderNames.AcceptEncoding, StringComparison.OrdinalIgnoreCase))
            {
                AppendRaw(hasher, PrefixEnc);
                AppendStringValues(hasher, values, ref byteBuffer, ref rentedBytes, ref charBuffer, ref rentedChars);
            }
            else
            {
                AppendRaw(hasher, PrefixHdr);
                AppendString(hasher, headerName, ref byteBuffer, ref rentedBytes, ref charBuffer, ref rentedChars, lowercase: true);
                AppendRaw(hasher, EqualsSign);
                AppendStringValues(hasher, values, ref byteBuffer, ref rentedBytes, ref charBuffer, ref rentedChars);
            }
        }

        // Named values (auth-user, hashed cookies/headers, contributor values) — sorted for stability.
        if (vary.Values.Count > 0)
        {
            string[] valueKeys = vary.Values.Keys.ToArray();
            Array.Sort(valueKeys, StringComparer.Ordinal);
            for (int i = 0; i < valueKeys.Length; i++)
            {
                string key = valueKeys[i];
                AppendRaw(hasher, PrefixVal);
                AppendString(hasher, key, ref byteBuffer, ref rentedBytes, ref charBuffer, ref rentedChars, lowercase: false);
                AppendRaw(hasher, Colon);
                AppendString(hasher, vary.Values[key], ref byteBuffer, ref rentedBytes, ref charBuffer, ref rentedChars, lowercase: false);
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void AppendRaw(XxHash3 hasher, ReadOnlySpan<byte> data) => hasher.Append(data);

    private static void AppendString(
        XxHash3 hasher,
        string? value,
        ref Span<byte> byteBuffer,
        ref byte[]? rentedBytes,
        ref Span<char> charBuffer,
        ref char[]? rentedChars,
        bool lowercase)
    {
        if (string.IsNullOrEmpty(value))
            return;

        ReadOnlySpan<char> source = value.AsSpan();

        if (lowercase)
        {
            if (source.Length > charBuffer.Length)
            {
                if (rentedChars != null)
                    ArrayPool<char>.Shared.Return(rentedChars);
                rentedChars = ArrayPool<char>.Shared.Rent(source.Length);
                charBuffer = rentedChars;
            }

            int written = source.ToLowerInvariant(charBuffer);
            source = charBuffer[..written];
        }

        int maxBytes = Encoding.UTF8.GetMaxByteCount(source.Length);
        if (maxBytes > byteBuffer.Length)
        {
            if (rentedBytes != null)
                ArrayPool<byte>.Shared.Return(rentedBytes);
            rentedBytes = ArrayPool<byte>.Shared.Rent(maxBytes);
            byteBuffer = rentedBytes;
        }

        int bytesWritten = Encoding.UTF8.GetBytes(source, byteBuffer);
        hasher.Append(byteBuffer[..bytesWritten]);
    }

    private static void AppendStringValues(
        XxHash3 hasher,
        StringValues values,
        ref Span<byte> byteBuffer,
        ref byte[]? rentedBytes,
        ref Span<char> charBuffer,
        ref char[]? rentedChars)
    {
        if (values.Count == 0)
            return;

        if (values.Count == 1)
        {
            AppendString(hasher, values[0], ref byteBuffer, ref rentedBytes, ref charBuffer, ref rentedChars, lowercase: true);
            return;
        }

        for (int i = 0; i < values.Count; i++)
        {
            if (i > 0)
                AppendRaw(hasher, Comma);
            AppendString(hasher, values[i], ref byteBuffer, ref rentedBytes, ref charBuffer, ref rentedChars, lowercase: true);
        }
    }
}
