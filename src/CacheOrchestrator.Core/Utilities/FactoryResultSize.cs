using System.Buffers;
using System.Text;

namespace CacheOrchestrator.Utilities;

/// <summary>
/// Cheap, optional size estimation for factory results. Returns null when size is unknown
/// without expensive serialization.
/// </summary>
internal static class FactoryResultSize
{
    /// <summary>
    /// Estimates payload size in bytes for known shapes (string UTF-8, byte buffers, seekable streams).
    /// </summary>
    public static long? TryEstimateBytes<T>(T? value)
    {
        if (value is null)
            return null;

        switch (value)
        {
            case string s:
                return Encoding.UTF8.GetByteCount(s);
            case byte[] bytes:
                return bytes.LongLength;
            case Memory<byte> mem:
                return mem.Length;
            case ReadOnlyMemory<byte> rom:
                return rom.Length;
            case ArraySegment<byte> seg:
                return seg.Count;
            case ReadOnlySequence<byte> seq:
                return seq.Length;
            case Stream stream when stream.CanSeek:
                try
                {
                    long len = stream.Length;
                    return len >= 0 ? len : null;
                }
                catch
                {
                    return null;
                }
            default:
                return null;
        }
    }
}
