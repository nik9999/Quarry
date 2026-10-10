using Quarry.Core.Numerics;
using Xunit;

namespace Quarry.Core.Tests.Numerics;

public class CosineKernelTests
{
    [Fact]
    public void CosineSimilarity_IdenticalVectors_ReturnsOne()
    {
        float[] a = [1.0f, 2.0f, 3.0f, 4.0f];
        float[] b = [1.0f, 2.0f, 3.0f, 4.0f];

        float similarity = CosineKernel.CosineSimilarity(a, b);

        Assert.Equal(1.0f, similarity, precision: 5);
    }

    [Fact]
    public void CosineSimilarity_OrthogonalVectors_ReturnsZero()
    {
        float[] a = [1.0f, 0.0f, 0.0f];
        float[] b = [0.0f, 1.0f, 0.0f];

        float similarity = CosineKernel.CosineSimilarity(a, b);

        Assert.Equal(0.0f, similarity, precision: 5);
    }

    [Fact]
    public void CosineSimilarity_OppositeVectors_ReturnsMinusOne()
    {
        float[] a = [1.0f, 2.0f, 3.0f];
        float[] b = [-1.0f, -2.0f, -3.0f];

        float similarity = CosineKernel.CosineSimilarity(a, b);

        Assert.Equal(-1.0f, similarity, precision: 5);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(7)]
    [InlineData(15)]
    [InlineData(16)]
    [InlineData(17)]
    [InlineData(384)]
    [InlineData(512)]
    public void CosineSimilarity_ArbitraryDimensions_HandlesSimdAndRemainderCorrectly(int dimensions)
    {
        var a = new float[dimensions];
        var b = new float[dimensions];

        for (int i = 0; i < dimensions; i++)
        {
            a[i] = (i + 1) * 0.1f;
            b[i] = (i + 1) * 0.2f; // Scaled identical direction
        }

        float similarity = CosineKernel.CosineSimilarity(a, b);

        Assert.Equal(1.0f, similarity, precision: 4);
    }

    [Fact]
    public void CosineSimilarity_DifferentLengths_ThrowsArgumentException()
    {
        float[] a = [1.0f, 2.0f];
        float[] b = [1.0f, 2.0f, 3.0f];

        Assert.Throws<ArgumentException>(() => CosineKernel.CosineSimilarity(a, b));
    }

    [Fact]
    public void CosineSimilarity_EmptySpans_ReturnsZero()
    {
        float[] a = [];
        float[] b = [];

        float similarity = CosineKernel.CosineSimilarity(a, b);

        Assert.Equal(0.0f, similarity);
    }

    [Fact]
    public void CosineSimilarity_ZeroNormVector_ReturnsZero()
    {
        float[] a = [0.0f, 0.0f, 0.0f];
        float[] b = [1.0f, 2.0f, 3.0f];

        float similarity = CosineKernel.CosineSimilarity(a, b);

        Assert.Equal(0.0f, similarity);
    }
}
