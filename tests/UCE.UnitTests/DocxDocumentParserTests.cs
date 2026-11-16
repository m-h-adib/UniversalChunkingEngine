using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using W = DocumentFormat.OpenXml.Wordprocessing;
using UCE.Application.Documents.Abstractions;
using UCE.Infrastructure.Documents.Parsing;
using Xunit;

namespace UCE.UnitTests;

public sealed class DocxDocumentParserTests
{
    [Fact]
    public async Task SkipsTocEntriesAndKeepsRealHeadingAndBody()
    {
        using var stream = CreateDocument(
            new W.Paragraph(
                new W.ParagraphProperties(new W.ParagraphStyleId { Val = "TOC1" }),
                new W.Run(new W.Text("فصل اول: احرام")),
                new W.Run(new W.FieldCode(" PAGEREF _Toc123 \\h "))),
            new W.Paragraph(
                new W.ParagraphProperties(new W.ParagraphStyleId { Val = "Heading1" }),
                new W.Run(new W.Text("فصل اول: احرام"))),
            new W.Paragraph(new W.Run(new W.Text("متن واقعی فصل"))));

        var parser = new DocxDocumentParser(new StubLanguageDetector());
        var result = await parser.ParseAsync(stream, "sample.docx");

        Assert.Equal(2, result.ParsedParagraphs.Count);
        Assert.Equal("فصل اول: احرام", result.ParsedParagraphs[0].Text);
        Assert.Equal("متن واقعی فصل", result.ParsedParagraphs[1].Text);
        Assert.DoesNotContain("PAGEREF", result.FullText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CapturesExplicitPageBreakNumbersAsBestEffort()
    {
        using var stream = CreateDocument(
            new W.Paragraph(new W.Run(new W.Text("صفحه اول"))),
            new W.Paragraph(new W.Run(new W.Break { Type = W.BreakValues.Page })),
            new W.Paragraph(new W.Run(new W.Text("صفحه دوم"))));

        var parser = new DocxDocumentParser(new StubLanguageDetector());
        var result = await parser.ParseAsync(stream, "sample.docx");

        Assert.Equal(1, result.ParsedParagraphs[0].PageNumber);
        Assert.Equal(2, result.ParsedParagraphs[1].PageNumber);
    }

    private static MemoryStream CreateDocument(params OpenXmlElement[] elements)
    {
        var stream = new MemoryStream();
        using (var word = WordprocessingDocument.Create(
            stream, WordprocessingDocumentType.Document, true))
        {
            var main = word.AddMainDocumentPart();
            main.Document = new W.Document(new W.Body(elements));
            main.Document.Save();
        }

        stream.Position = 0;
        return stream;
    }

    private sealed class StubLanguageDetector : ITextLanguageDetector
    {
        public UCE.Domain.Enums.DocumentLanguage Detect(string text) =>
            UCE.Domain.Enums.DocumentLanguage.Persian;
    }
}