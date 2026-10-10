using Quarry.Ingestion.Embeddings.Onnx;
using Xunit;

namespace Quarry.Ingestion.Tests.Embeddings;

public class TokenMappersTests
{
    [Fact]
    public void Direct_PreservesTokenIdsExactly()
    {
        int[] raw = [101, 2054, 102];
        var mapped = TokenMappers.Direct(raw);

        long[] expected = [101L, 2054L, 102L];
        Assert.Equal(expected, mapped);
    }

    [Fact]
    public void XlmRoberta_AddsBosEosAndShiftsTokenIdsByOne()
    {
        int[] raw = [10, 20, 30];
        var mapped = TokenMappers.XlmRoberta(raw);

        // Expected: [<s> (0), 11, 21, 31, </s> (2)]
        long[] expected = [0L, 11L, 21L, 31L, 2L];
        Assert.Equal(expected, mapped);
    }

    [Fact]
    public void XlmRoberta_EmptyInput_ReturnsOnlyBosAndEos()
    {
        int[] raw = [];
        var mapped = TokenMappers.XlmRoberta(raw);

        long[] expected = [0L, 2L];
        Assert.Equal(expected, mapped);
    }
}
