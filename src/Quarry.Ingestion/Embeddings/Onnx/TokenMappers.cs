namespace Quarry.Ingestion.Embeddings.Onnx;

/// <summary>
/// Стандартные преобразователи токенов для различных архитектур нейросетей.
/// </summary>
public static class TokenMappers
{
    /// <summary>
    /// Преобразование по умолчанию (точный перенос токенов без сдвига). Подходит для классических BERT моделей.
    /// </summary>
    public static IReadOnlyList<long> Direct(IReadOnlyList<int> rawTokens)
    {
        var result = new long[rawTokens.Count];
        for (int i = 0; i < rawTokens.Count; i++)
        {
            result[i] = rawTokens[i];
        }
        return result;
    }

    /// <summary>
    /// Преобразование для XLM-RoBERTa / Multilingual-E5 при использовании сырого SentencePiece словаря.
    /// Добавляет BOS (0), смещает токены подслов на +1 и завершает EOS (2).
    /// </summary>
    public static IReadOnlyList<long> XlmRoberta(IReadOnlyList<int> rawTokens)
    {
        var result = new List<long>(rawTokens.Count + 2)
        {
            0L // <s>
        };

        for (int i = 0; i < rawTokens.Count; i++)
        {
            result.Add(rawTokens[i] + 1L);
        }

        result.Add(2L); // </s>
        return result;
    }
}
