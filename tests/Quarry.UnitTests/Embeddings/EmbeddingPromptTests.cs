using Quarry.Core.Abstractions;
using Xunit;

namespace Quarry.Core.Tests.Abstractions;

public class EmbeddingPromptTests
{
    [Fact]
    public void NonePrompt_ReturnsTextUnchanged()
    {
        var prompt = EmbeddingPrompt.None;

        Assert.Equal("Sample text", prompt.Apply("Sample text", EmbeddingTaskType.Document));
        Assert.Equal("Sample query", prompt.Apply("Sample query", EmbeddingTaskType.Query));
    }

    [Fact]
    public void E5Prompt_AppliesPassageAndQueryPrefixes()
    {
        var prompt = EmbeddingPrompt.E5;

        Assert.Equal("passage: Текст документа", prompt.Apply("Текст документа", EmbeddingTaskType.Document));
        Assert.Equal("query: Вопрос пользователя", prompt.Apply("Вопрос пользователя", EmbeddingTaskType.Query));
    }

    [Fact]
    public void BgePrompt_AppliesPrefixOnlyToQuery()
    {
        var prompt = EmbeddingPrompt.Bge;

        Assert.Equal("Текст документа", prompt.Apply("Текст документа", EmbeddingTaskType.Document));
        Assert.Equal("Represent this sentence for searching relevant passages: Вопрос", prompt.Apply("Вопрос", EmbeddingTaskType.Query));
    }

    [Fact]
    public void NomicPrompt_AppliesSearchPrefixes()
    {
        var prompt = EmbeddingPrompt.Nomic;

        Assert.Equal("search_document: doc text", prompt.Apply("doc text", EmbeddingTaskType.Document));
        Assert.Equal("search_query: query text", prompt.Apply("query text", EmbeddingTaskType.Query));
    }
}
