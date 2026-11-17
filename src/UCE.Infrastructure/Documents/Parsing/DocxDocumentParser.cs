using DocumentFormat.OpenXml.Packaging;
using System.Text;
using W = DocumentFormat.OpenXml.Wordprocessing;
using UCE.Application.Documents.Abstractions;
using UCE.Application.Documents.Models;

namespace UCE.Infrastructure.Documents.Parsing;

public sealed class DocxDocumentParser(ITextLanguageDetector languageDetector) : IDocumentParser
{
    public bool CanParse(string fileName) =>
        Path.GetExtension(fileName).Equals(".docx", StringComparison.OrdinalIgnoreCase);

    public Task<ParsedDocument> ParseAsync(
        Stream stream,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var word = WordprocessingDocument.Open(stream, false);
        var body = word.MainDocumentPart?.Document?.Body
            ?? throw new InvalidDataException("DOCX document body is missing.");

        var document = new ParsedDocument
        {
            SourceFileName = Path.GetFileName(fileName),
            Title = word.PackageProperties.Title ?? Path.GetFileNameWithoutExtension(fileName),
            Author = word.PackageProperties.Creator
        };

        var textParts = new List<string>();
        var order = 0;
        var position = 0;
        var pageNumber = 1;
        var hasContentOnCurrentPage = false;

        foreach (var element in body.Elements())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (element is W.Paragraph paragraph)
            {
                var style = paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value;
                var isPageBreakBefore = paragraph.ParagraphProperties?.PageBreakBefore is not null;
                if (isPageBreakBefore && hasContentOnCurrentPage)
                {
                    pageNumber++;
                    hasContentOnCurrentPage = false;
                }

                if (IsTableOfContentsParagraph(paragraph, style))
                {
                    AdvancePageMarkers(paragraph, ref pageNumber, ref hasContentOnCurrentPage);
                    continue;
                }

                var text = ExtractParagraphText(paragraph);
                if (text.Length > 0)
                {
                    var paragraphIndex = document.ParsedParagraphs.Count;
                    var start = position;
                    var end = start + text.Length;

                    document.ParsedParagraphs.Add(new ParsedParagraph
                    {
                        Text = text,
                        Order = order,
                        StartPosition = start,
                        EndPosition = end,
                        StyleName = style,
                        PageNumber = pageNumber
                    });

                    document.Blocks.Add(new ParsedBlock
                    {
                        Order = order++,
                        Kind = ParsedBlockKind.Paragraph,
                        ParagraphIndex = paragraphIndex,
                        StartPosition = start,
                        EndPosition = end,
                        PageNumber = pageNumber
                    });

                    textParts.Add(text);
                    position = end + 2;
                    hasContentOnCurrentPage = true;
                }

                AdvancePageMarkers(paragraph, ref pageNumber, ref hasContentOnCurrentPage);
            }
            else if (element is W.Table table)
            {
                var parsedTable = ParseTable(table, document.Tables.Count);
                var tableIndex = document.Tables.Count;
                var tableText = FormatTable(parsedTable);
                if (string.IsNullOrWhiteSpace(tableText))
                    continue;

                var start = position;
                var end = start + tableText.Length;

                document.Tables.Add(parsedTable);
                document.Blocks.Add(new ParsedBlock
                {
                    Order = order++,
                    Kind = ParsedBlockKind.Table,
                    TableIndex = tableIndex,
                    StartPosition = start,
                    EndPosition = end,
                    PageNumber = pageNumber
                });

                textParts.Add(tableText);
                position = end + 2;
                hasContentOnCurrentPage = true;
            }
        }

        document.FullText = string.Join("\n\n", textParts);
        document.Language = languageDetector.Detect(document.FullText);
        document.Metadata["PageNumberSource"] = "DOCX rendered/explicit page-break markers (best effort)";
        return Task.FromResult(document);
    }

    private static bool IsTableOfContentsParagraph(W.Paragraph paragraph, string? style)
    {
        if (!string.IsNullOrWhiteSpace(style)
            && (style.StartsWith("TOC", StringComparison.OrdinalIgnoreCase)
                || style.StartsWith("TableOfContents", StringComparison.OrdinalIgnoreCase)))
            return true;

        // In Open XML, w:instrText is represented by FieldCode.
        var fieldInstructions = string.Join(" ",
            paragraph.Descendants<W.FieldCode>().Select(x => x.Text));
        var searchable = string.Concat(ExtractParagraphText(paragraph), " ", fieldInstructions);

        return searchable.Contains("PAGEREF", StringComparison.OrdinalIgnoreCase)
            || searchable.Contains("_Toc", StringComparison.OrdinalIgnoreCase)
            || fieldInstructions.Contains(" TOC ", StringComparison.OrdinalIgnoreCase)
            || fieldInstructions.StartsWith("TOC ", StringComparison.OrdinalIgnoreCase);
    }


    /// <summary>
    /// Reads WordprocessingML text only. OpenXML's InnerText also includes
    /// non-text DrawingML values such as floating-image coordinates.
    /// </summary>
    private static string ExtractParagraphText(W.Paragraph paragraph)
    {
        var builder = new StringBuilder();

        foreach (var node in paragraph.Descendants())
        {
            switch (node)
            {
                case W.Text text:
                    builder.Append(text.Text);
                    break;
                case W.TabChar:
                    builder.Append('\t');
                    break;
                case W.Break:
                case W.CarriageReturn:
                    builder.AppendLine();
                    break;
            }
        }

        return builder.ToString().Trim();
    }

    private static void AdvancePageMarkers(
        W.Paragraph paragraph,
        ref int pageNumber,
        ref bool hasContentOnCurrentPage)
    {
        var renderedBreaks = paragraph.Descendants<W.LastRenderedPageBreak>().Count();
        var explicitBreaks = paragraph.Descendants<W.Break>()
            .Count(x => x.Type?.Value == W.BreakValues.Page);

        var breaks = Math.Max(renderedBreaks, explicitBreaks);
        if (breaks == 0) return;

        pageNumber += breaks;
        hasContentOnCurrentPage = false;
    }

    private static ParsedTable ParseTable(W.Table table, int order)
    {
        var parsed = new ParsedTable { Order = order };
        var rowIndex = 0;
        var maxColumns = 0;

        foreach (var row in table.Elements<W.TableRow>())
        {
            var parsedRow = new ParsedTableRow { Index = rowIndex };
            var columnIndex = 0;

            foreach (var cell in row.Elements<W.TableCell>())
            {
                var text = string.Join(Environment.NewLine,
                    cell.Elements<W.Paragraph>().Select(ExtractParagraphText)
                        .Where(x => !string.IsNullOrWhiteSpace(x))).Trim();

                var span = Math.Max(1, cell.TableCellProperties?.GridSpan?.Val?.Value ?? 1);
                parsedRow.Cells.Add(new ParsedTableCell
                {
                    RowIndex = rowIndex,
                    ColumnIndex = columnIndex,
                    Text = text,
                    ColumnSpan = span,
                    RowSpan = 1
                });
                columnIndex += span;
            }

            maxColumns = Math.Max(maxColumns, columnIndex);
            parsed.Rows.Add(parsedRow);
            rowIndex++;
        }

        parsed.RowCount = rowIndex;
        parsed.ColumnCount = maxColumns;
        return parsed;
    }

    private static string FormatTable(ParsedTable table) =>
        string.Join(Environment.NewLine, table.Rows.Select((row, rowIndex) =>
            $"ردیف {rowIndex + 1}: " +
            string.Join(" | ", row.Cells.Select(cell =>
                $"ستون {cell.ColumnIndex + 1}: {cell.Text}"))));
}