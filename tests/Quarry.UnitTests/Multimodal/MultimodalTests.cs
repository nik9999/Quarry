namespace Quarry.Ingestion.Tests.Multimodal;

using Quarry.Ingestion.Chunking;
using Quarry.Ingestion.Multimodal;
using Xunit;

public class MultimodalTests
{
    private sealed class FakePdfExtractor : IPdfImageExtractor
    {
        public IReadOnlyList<PdfExtractedPage> Extract(Stream pdfStream)
        {
            var images = new List<PdfExtractedImage>
            {
                new(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, "image/png", 100, 100, "diagram")
            };

            return new List<PdfExtractedPage>
            {
                new(1, "Текст страницы один с диаграммой.", images)
            };
        }

        public IReadOnlyList<PdfExtractedPage> Extract(byte[] pdfBytes)
        {
            using var stream = new MemoryStream(pdfBytes);
            return Extract(stream);
        }
    }

    [Fact]
    public void MultimodalChunk_ToDocumentChunk_FormatsCorrectly()
    {
        var meta = new Dictionary<string, string> { ["source"] = "manual.pdf" };
        var mmChunk = new MultimodalChunk(
            ChunkId: "c-1",
            DocumentId: "d-1",
            Index: 0,
            Text: "Основной текст страницы",
            PageNumber: 3,
            ImageData: new byte[] { 1, 2, 3 },
            ImageMimeType: "image/png",
            ImageCaption: "Схема архитектуры RAG",
            Metadata: meta,
            StartCharOffset: 10,
            EndCharOffset: 34);

        var docChunk = mmChunk.ToDocumentChunk();

        Assert.Equal("c-1", docChunk.ChunkId);
        Assert.Equal("d-1", docChunk.DocumentId);
        Assert.Equal(0, docChunk.Index);
        Assert.Equal(10, docChunk.StartCharOffset);
        Assert.Equal(34, docChunk.EndCharOffset);
        Assert.Equal("3", docChunk.Metadata?["page_number"]);
        Assert.Equal("true", docChunk.Metadata?["has_image"]);
        Assert.Equal("Схема архитектуры RAG", docChunk.Metadata?["image_caption"]);
        Assert.Contains("[Изображение: Схема архитектуры RAG]", docChunk.Text);
    }

    [Fact]
    public async Task MultimodalChunker_ChunkPdfAsync_ExtractsPagesAndImages()
    {
        var fakeExtractor = new FakePdfExtractor();
        var captioner = new DefaultImageCaptioner((bytes, mime, _) => Task.FromResult("График метрик RAG"));
        var textChunker = new FixedSizeChunker(50, 0);

        var multimodalChunker = new MultimodalChunker(fakeExtractor, captioner, textChunker);

        using var dummyStream = new MemoryStream();
        var chunks = await multimodalChunker.ChunkPdfAsync("pdf-doc", dummyStream).ToListAsync();

        Assert.True(chunks.Count >= 2);
        Assert.Contains(chunks, c => c.ImageData is not null && c.ImageCaption == "График метрик RAG");
        Assert.Contains(chunks, c => c.ImageData is null && c.Text.Contains("Текст страницы один"));
    }
}
