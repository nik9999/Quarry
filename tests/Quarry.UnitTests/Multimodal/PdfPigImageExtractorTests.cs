namespace Quarry.Ingestion.Tests.Multimodal;

using UglyToad.PdfPig.Writer;
using UglyToad.PdfPig.Core;
using Quarry.Ingestion.Multimodal;
using Xunit;

public class PdfPigImageExtractorTests
{
    [Fact]
    public void Extract_ValidPdfWithText_ExtractsPagesCorrectly()
    {
        var builder = new PdfDocumentBuilder();
        var page = builder.AddPage(UglyToad.PdfPig.Content.PageSize.A4);
        var font = builder.AddStandard14Font(UglyToad.PdfPig.Fonts.Standard14Fonts.Standard14Font.Helvetica);
        page.AddText("Hello Multimodal Quarry PDF", 12, new PdfPoint(50, 700), font);

        var pdfBytes = builder.Build();

        var extractor = new PdfPigImageExtractor();
        var pages = extractor.Extract(pdfBytes);

        Assert.Single(pages);
        Assert.Equal(1, pages[0].PageNumber);
        Assert.Contains("Hello Multimodal Quarry PDF", pages[0].Text);
    }
}
