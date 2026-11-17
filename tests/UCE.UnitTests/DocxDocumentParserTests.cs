using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using System.IO.Compression;
using System.Text;
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


    [Fact]
    public async Task ExtractsWordTextButIgnoresFloatingImageCoordinates()
    {
        const string drawingXml =
            "<w:drawing xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\" " +
            "xmlns:wp=\"http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing\">" +
            "<wp:anchor><wp:positionH><wp:posOffset>397530</wp:posOffset></wp:positionH>" +
            "<wp:positionV><wp:posOffset>236771</wp:posOffset></wp:positionV></wp:anchor></w:drawing>";

        var paragraph = new W.Paragraph(
            new W.Run(new W.Text("متن قبل")),
            new W.Run(new W.TabChar()),
            new W.Run(new W.Text("متن بعد")),
            new W.Run(new W.Break()),
            new W.Run(new W.Text("سطر دوم")));

        using var stream = CreateDocument(paragraph);
        InjectDrawingMetadata(stream, drawingXml);
        var parser = new DocxDocumentParser(new StubLanguageDetector());
        var result = await parser.ParseAsync(stream, "sample.docx");

        Assert.Single(result.ParsedParagraphs);
        Assert.Equal("متن قبل\tمتن بعد" + Environment.NewLine + "سطر دوم",
            result.ParsedParagraphs[0].Text);
        Assert.DoesNotContain("397530", result.FullText);
        Assert.DoesNotContain("236771", result.FullText);
    }

    [Fact]
    public async Task IgnoresFloatingImageCoordinatesInsideTableCells()
    {
        const string drawingXml =
            "<w:drawing xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\" " +
            "xmlns:wp=\"http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing\">" +
            "<wp:anchor><wp:positionH><wp:posOffset>-30480</wp:posOffset></wp:positionH></wp:anchor></w:drawing>";

        var table = new W.Table(
            new W.TableRow(
                new W.TableCell(
                    new W.Paragraph(new W.Run(new W.Text("متن سلول"))))));

        using var stream = CreateDocument(table);
        InjectDrawingMetadata(stream, drawingXml);
        var parser = new DocxDocumentParser(new StubLanguageDetector());
        var result = await parser.ParseAsync(stream, "sample.docx");

        Assert.Single(result.Tables);
        Assert.Contains("متن سلول", result.Tables[0].Rows[0].Cells[0].Text);
        Assert.DoesNotContain("-30480", result.Tables[0].Rows[0].Cells[0].Text);
        Assert.DoesNotContain("-30480", result.FullText);
    }


    private static void InjectDrawingMetadata(MemoryStream stream, string drawingXml)
    {
        stream.Position = 0;

        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            var documentEntry = archive.GetEntry("word/document.xml")
                ?? throw new InvalidDataException("DOCX document.xml is missing.");

            string documentXml;
            using (var reader = new StreamReader(documentEntry.Open()))
                documentXml = reader.ReadToEnd();

            documentEntry.Delete();

            var updatedEntry = archive.CreateEntry("word/document.xml");
            using (var writer = new StreamWriter(updatedEntry.Open(), new UTF8Encoding(false)))
            {
                var drawingRun = "<w:r>" + drawingXml + "</w:r>";
                var updatedXml = documentXml.Replace(
                    "</w:p>",
                    drawingRun + "</w:p>",
                    StringComparison.Ordinal);

                writer.Write(updatedXml);
            }
        }

        stream.Position = 0;
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