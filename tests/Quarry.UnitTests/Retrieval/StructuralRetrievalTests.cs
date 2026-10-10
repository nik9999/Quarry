namespace Quarry.Retrieval.Tests.Vectorless;

using Quarry.Retrieval.Vectorless;
using Xunit;

public class StructuralRetrievalTests
{
    private const string SampleMarkdown = """
    # Архитектура Quarry

    Введение в систему и базовые концепции.

    ## Глава 1. Компоненты системы

    Описание основных компонентов. [см. § 2.1]

    ### § 1.1. Векторное ядро

    Детали реализации sqlite-vec.

    ### § 1.2. Лексический поиск

    Детали BM25 ретривера.

    ## Глава 2. Пайплайны

    Общее описание конвейеров.

    ### § 2.1. Конвейер индексации

    Описание Ingestion пайплайна.
    """;

    [Fact]
    public void DocumentStructureParser_ParsesHierarchyAndCrossReferences()
    {
        var index = DocumentStructureParser.Parse("quarry-doc", SampleMarkdown);

        Assert.NotNull(index.Root);
        Assert.True(index.AllNodes.Count >= 6);

        var sec11 = index.Lookup("§1.1");
        Assert.NotNull(sec11);
        Assert.Equal("§ 1.1. Векторное ядро", sec11.Title);
        Assert.Contains("Детали реализации sqlite-vec", sec11.Text);

        var chap1 = index.Lookup("глава1");
        Assert.NotNull(chap1);
        Assert.Contains("§2.1", chap1.CrossReferences);
    }

    [Fact]
    public void StructuralRetriever_NavigatesAncestorsAndDescendants()
    {
        var index = DocumentStructureParser.Parse("quarry-doc", SampleMarkdown);
        var retriever = new StructuralRetriever(index);

        var chap1 = index.Lookup("глава1");
        Assert.NotNull(chap1);

        var descendants = retriever.Descendants(chap1.Id);
        Assert.Equal(2, descendants.Count);
        Assert.Contains(descendants, d => d.Id == "§1.1");
        Assert.Contains(descendants, d => d.Id == "§1.2");

        var sec11 = index.Lookup("§1.1");
        Assert.NotNull(sec11);
        var ancestors = retriever.Ancestors(sec11.Id);
        Assert.True(ancestors.Count >= 2);
        Assert.Equal("глава1", ancestors[0].Id);
    }

    [Fact]
    public async Task StructuralRetriever_RetrieveAsync_FindsExactSection()
    {
        var index = DocumentStructureParser.Parse("quarry-doc", SampleMarkdown);
        var retriever = new StructuralRetriever(index);

        var results = await retriever.RetrieveAsync("Что описано в § 1.1?", 3);

        Assert.NotEmpty(results);
        Assert.Equal("§1.1", results[0].Chunk.ChunkId);
        Assert.Contains("sqlite-vec", results[0].Chunk.Text);
    }

    [Fact]
    public async Task LlmSectionRouter_RoutesToAppropriateSection()
    {
        var index = DocumentStructureParser.Parse("quarry-doc", SampleMarkdown);
        var fakeChat = new Fakes.FakeChatClient(_ => "§1.2\n§2.1");
        var router = new LlmSectionRouter(index, fakeChat);

        var sections = await router.RouteAsync("Как устроен BM25 поиск?", maxSections: 2);

        Assert.Equal(2, sections.Count);
        Assert.Equal("§1.2", sections[0].Id);
        Assert.Equal("§2.1", sections[1].Id);
    }
}
