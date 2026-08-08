using CacheOrchestrator.Configuration;
using CacheOrchestrator.Utilities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.Primitives;
using System.Buffers;
using System.IO.Hashing;
using System.Runtime.CompilerServices;
using System.Text;

namespace CacheOrchestrator.FusionCache;

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
    private static readonly byte[] PrefixScheme = "|s:"u8.ToArray();
    private static readonly byte[] PrefixHost = "|h:"u8.ToArray();
    private static readonly byte[] EqualsSign = "="u8.ToArray();
    private static readonly byte[] Comma = ","u8.ToArray();

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

        try
        {
            // 0. Explicit resource id (CRUD / entity-scoped keys) — when set, prefer stable id segment.
            if (http.Items.TryGetValue(CacheOrchestratorKeys.ResourceIdKey, out object? ridObj)
                && ridObj is string resourceId
                && resourceId.Length > 0)
            {
                AppendRaw(hasher, "id:"u8);
                AppendString(hasher, resourceId, ref byteBuffer, ref rentedBytes, ref charBuffer, ref rentedChars, lowercase: false);

                // Still vary on encoding / public address when configured.
                if (opts.FusionCacheVaryOnEncoding)
                {
                    StringValues ae = http.Request.Headers.AcceptEncoding;
                    if (ae.Count > 0)
                    {
                        AppendRaw(hasher, PrefixEnc);
                        AppendStringValues(hasher, ae, ref byteBuffer, ref rentedBytes, ref charBuffer, ref rentedChars);
                    }
                }

                if (opts.FusionCacheVaryOnPublicAddress)
                {
                    AppendRaw(hasher, PrefixScheme);
                    AppendString(hasher, http.Request.Scheme, ref byteBuffer, ref rentedBytes, ref charBuffer, ref rentedChars, lowercase: true);

                    AppendRaw(hasher, PrefixHost);
                    AppendString(hasher, http.Request.Host.Value, ref byteBuffer, ref rentedBytes, ref charBuffer, ref rentedChars, lowercase: true);
                }

                ulong resourceHash = hasher.GetCurrentHashAsUInt64();
                return string.Create(null, stackalloc char[160], $"{opts.Domain}:{opts.VersionHex}:id:{resourceId}:{resourceHash:x16}");
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

            // 2. Query
            IQueryCollection query = http.Request.Query;
            if (query.Count > 0)
            {
                string[] keys = ArrayPool<string>.Shared.Rent(query.Count);
                int keyCount = 0;

                try
                {
                    foreach (string key in query.Keys)
                    {
                        if (!HttpHelper.IsTrackingParameter(key))
                            keys[keyCount++] = key;
                    }

                    if (keyCount > 1)
                        Array.Sort(keys, 0, keyCount, StringComparer.OrdinalIgnoreCase);

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

            // 3. Accept-Encoding
            if (opts.FusionCacheVaryOnEncoding)
            {
                StringValues ae = http.Request.Headers.AcceptEncoding;
                if (ae.Count > 0)
                {
                    AppendRaw(hasher, PrefixEnc);
                    AppendStringValues(hasher, ae, ref byteBuffer, ref rentedBytes, ref charBuffer, ref rentedChars);
                }
            }

            // 4. Public address
            if (opts.FusionCacheVaryOnPublicAddress)
            {
                AppendRaw(hasher, PrefixScheme);
                AppendString(hasher, http.Request.Scheme, ref byteBuffer, ref rentedBytes, ref charBuffer, ref rentedChars, lowercase: true);

                AppendRaw(hasher, PrefixHost);
                AppendString(hasher, http.Request.Host.Value, ref byteBuffer, ref rentedBytes, ref charBuffer, ref rentedChars, lowercase: true);
            }

            ulong hash = hasher.GetCurrentHashAsUInt64();
            return string.Create(null, stackalloc char[128], $"{opts.Domain}:{opts.VersionHex}:{hash:x16}");
        }
        finally
        {
            if (rentedBytes != null)
                ArrayPool<byte>.Shared.Return(rentedBytes);
            if (rentedChars != null)
                ArrayPool<char>.Shared.Return(rentedChars);
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