using System.Text;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using UCE.Application.Documents.Abstractions;
using UCE.Application.Documents.Models;

namespace UCE.Infrastructure.Documents.Parsing;

public sealed class PdfDocumentParser(ITextNormalizer normalizer, ITextLanguageDetector languageDetector) : IDocumentParser
{
    public bool CanParse(string fileName) =>
        Path.GetExtension(fileName).Equals(".pdf", StringComparison.OrdinalIgnoreCase);

    public Task<ParsedDocument> ParseAsync(Stream stream, string fileName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var pdf = PdfDocument.Open(stream);

        var document = new ParsedDocument
        {
            SourceFileName = Path.GetFileName(fileName),
            Title = Path.GetFileNameWithoutExtension(fileName)
        };

        var fullText = new StringBuilder();
        var order = 0;

        foreach (var page in pdf.GetPages())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var pageText = normalizer.Normalize(page.Text ?? string.Empty);

            if (fullText.Length > 0)
                fullText.Append("\n\n");

            var pageStart = fullText.Length;
            fullText.Append(pageText);

            document.Pages.Add(new ParsedPage
            {
                PageNumber = page.Number, Text = pageText,
                StartPosition = pageStart, EndPosition = fullText.Length
            });

            var searchOffset = 0;
            foreach (var rawParagraph in Regex.Split(pageText, @"\n\s*\n"))
            {
                var text = rawParagraph.Trim();
                if (text.Length == 0) continue;

                var localStart = pageText.IndexOf(text, searchOffset, StringComparison.Ordinal);
                if (localStart < 0) continue;

                var start = pageStart + localStart;
                var end = start + text.Length;
                var paragraphIndex = document.ParsedParagraphs.Count;

                document.ParsedParagraphs.Add(new ParsedParagraph
                {
                    Text = text, Order = order, StartPosition = start,
                    EndPosition = end, PageNumber = page.Number
                });

                document.Blocks.Add(new ParsedBlock
                {
                    Order = order++, Kind = ParsedBlockKind.Paragraph,
                    ParagraphIndex = paragraphIndex, StartPosition = start,
                    EndPosition = end, PageNumber = page.Number
                });

                searchOffset = localStart + text.Length;
            }
        }

        document.FullText = fullText.ToString();
        document.Language = languageDetector.Detect(document.FullText);
        return Task.FromResult(document);
    }
}