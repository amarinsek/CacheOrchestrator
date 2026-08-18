using CacheOrchestrator.Utilities;
using System.Text;

namespace CacheOrchestrator.UnitTests.Utilities;

public class FactoryResultSizeTests
{
    [Fact]
    public void String_UsesUtf8ByteCount()
    {
        string s = "héllo";
        FactoryResultSize.TryEstimateBytes(s).Should().Be(Encoding.UTF8.GetByteCount(s));
    }

    [Fact]
    public void ByteArray_ReturnsLength()
    {
        FactoryResultSize.TryEstimateBytes(new byte[42]).Should().Be(42);
    }

    [Fact]
    public void AnonymousObject_ReturnsNull()
    {
        FactoryResultSize.TryEstimateBytes(new { a = 1 }).Should().BeNull();
    }
}
