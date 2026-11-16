using DocumentFormat.OpenXml.Packaging;
using W = DocumentFormat.OpenXml.Wordprocessing;
using UCE.Application.Documents.Abstractions;
using UCE.Application.Documents.Models;

namespace UCE.Infrastructure.Documents.Parsing;

public sealed class DocxDocumentParser(ITextLanguageDetector languageDetector) : IDocumentParser
{
    public bool CanParse(string fileName) =>
        Path.GetExtension(fileName).Equals(".docx", StringComparison.OrdinalIgnoreCase);

    public Task<ParsedDocument> ParseAsync(Stream stream, string fileName, CancellationToken cancellationToken = default)
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

        foreach (var element in body.Elements())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (element is W.Paragraph paragraph)
            {
                var text = paragraph.InnerText?.Trim() ?? string.Empty;
                if (text.Length == 0) continue;

                var style = paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value;
                var paragraphIndex = document.ParsedParagraphs.Count;
                var start = position;
                var end = start + text.Length;

                document.ParsedParagraphs.Add(new ParsedParagraph
                {
                    Text = text, Order = order, StartPosition = start,
                    EndPosition = end, StyleName = style
                });

                document.Blocks.Add(new ParsedBlock
                {
                    Order = order++, Kind = ParsedBlockKind.Paragraph,
                    ParagraphIndex = paragraphIndex, StartPosition = start, EndPosition = end
                });

                textParts.Add(text);
                position = end + 2;
            }
            else if (element is W.Table table)
            {
                var parsedTable = ParseTable(table, document.Tables.Count);
                var tableIndex = document.Tables.Count;
                var tableText = FormatTable(parsedTable);
                var start = position;
                var end = start + tableText.Length;

                document.Tables.Add(parsedTable);
                document.Blocks.Add(new ParsedBlock
                {
                    Order = order++, Kind = ParsedBlockKind.Table,
                    TableIndex = tableIndex, StartPosition = start, EndPosition = end
                });

                textParts.Add(tableText);
                position = end + 2;
            }
        }

        document.FullText = string.Join("\n\n", textParts);
        document.Language = languageDetector.Detect(document.FullText);
        return Task.FromResult(document);
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
                    cell.Elements<W.Paragraph>().Select(x => x.InnerText)
                        .Where(x => !string.IsNullOrWhiteSpace(x))).Trim();

                var span = Math.Max(1, cell.TableCellProperties?.GridSpan?.Val?.Value ?? 1);
                parsedRow.Cells.Add(new ParsedTableCell
                {
                    RowIndex = rowIndex, ColumnIndex = columnIndex,
                    Text = text, ColumnSpan = span, RowSpan = 1
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