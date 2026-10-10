using System.Numerics;

namespace Quarry.Core.Numerics;

/// <summary>
/// Высокопроизводительное вычисление косинусного сходства векторов с использованием аппаратного SIMD.
/// </summary>
public static class CosineKernel
{
    /// <summary>
    /// Вычисляет косинусное сходство между двумя векторами (диапазон [-1.0, 1.0]).
    /// </summary>
    public static float CosineSimilarity(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        if (a.Length != b.Length)
        {
            throw new ArgumentException($"Размерности векторов не совпадают: {a.Length} != {b.Length}");
        }

        if (a.Length == 0)
        {
            return 0.0f;
        }

        float dot = 0f;
        float normA = 0f;
        float normB = 0f;

        int i = 0;
        int vectorSize = Vector<float>.Count;

        if (Vector.IsHardwareAccelerated && a.Length >= vectorSize)
        {
            var vDot = Vector<float>.Zero;
            var vNormA = Vector<float>.Zero;
            var vNormB = Vector<float>.Zero;

            int limit = a.Length - vectorSize;
            while (i <= limit)
            {
                var va = new Vector<float>(a.Slice(i, vectorSize));
                var vb = new Vector<float>(b.Slice(i, vectorSize));

                vDot += va * vb;
                vNormA += va * va;
                vNormB += vb * vb;

                i += vectorSize;
            }

            dot = Vector.Dot(vDot, Vector<float>.One);
            normA = Vector.Dot(vNormA, Vector<float>.One);
            normB = Vector.Dot(vNormB, Vector<float>.One);
        }

        // Хвостовая обработка элементов, не кратных размеру SIMD-регистра
        for (; i < a.Length; i++)
        {
            float va = a[i];
            float vb = b[i];
            dot += va * vb;
            normA += va * va;
            normB += vb * vb;
        }

        float denominator = MathF.Sqrt(normA) * MathF.Sqrt(normB);
        return denominator <= 0f ? 0f : dot / denominator;
    }
}
