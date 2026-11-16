using Microsoft.Extensions.Options;
using UCE.Application.Documents.Abstractions;
using UCE.Application.Documents.Models;
using UCE.Domain.Enums;

namespace UCE.Infrastructure.Documents.Structure;

public sealed class DocumentStructureBuilder(
    IOptions<StructureBuilderOptions> options,
    IHeadingDetector headingDetector,
    IContentBlockClassifier classifier) : IDocumentStructureBuilder
{
    private readonly StructureBuilderOptions _options = options.Value;

    public DocumentStructure Build(ParsedDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var result = new DocumentStructure { FullText = document.FullText };
        var paragraphs = document.ParsedParagraphs.OrderBy(x => x.Order).ToList();

        if (paragraphs.Count == 0 && document.Pages.Count > 0)
            paragraphs = FromPages(document.Pages);
        if (paragraphs.Count == 0 && document.Tables.Count == 0)
            return result;

        var stack = new Stack<DocumentStructureSection>();
        if (_options.CreateRootSection)
        {
            var root = new DocumentStructureSection
            {
                Title = _options.RootSectionTitle, Level = 0,
                Order = 0, StartPosition = 0
            };
            result.Sections.Add(root);
            stack.Push(root);
        }

        var sectionOrder = 1;

        void AddParagraph(ParsedParagraph parsed)
        {
            var classification = classifier.Classify(parsed);
            var heading = headingDetector.Detect(parsed);
            var isHeading = heading.IsHeading
                && classification.Type != ContentBlockType.OrderedListItem
                && classification.Type != ContentBlockType.UnorderedListItem;

            if (isHeading)
            {
                var level = Math.Clamp(
                    heading.Level ?? _options.DefaultHeadingLevel,
                    1, _options.MaximumHeadingLevel);

                while (stack.Count > 0 && stack.Peek().Level >= level)
                {
                    var completed = stack.Pop();
                    completed.EndPosition = Math.Max(completed.StartPosition, parsed.StartPosition);
                }

                var section = new DocumentStructureSection
                {
                    Title = parsed.Text.Trim(), Level = level,
                    Order = sectionOrder++, StartPosition = parsed.StartPosition
                };

                if (stack.Count > 0)
                {
                    var parent = stack.Peek();
                    section.ParentId = parent.Id;
                    parent.Children.Add(section);
                }
                else result.Sections.Add(section);

                stack.Push(section);
                result.Paragraphs.Add(new DocumentParagraph
                {
                    Order = parsed.Order, Text = parsed.Text,
                    StartPosition = parsed.StartPosition, EndPosition = parsed.EndPosition,
                    PageNumber = parsed.PageNumber, IsHeading = true,
                    HeadingLevel = level, StyleName = parsed.StyleName,
                    Type = ContentBlockType.Heading, SectionId = section.Id
                });
                return;
            }

            var item = new DocumentParagraph
            {
                Order = parsed.Order, Text = parsed.Text,
                StartPosition = parsed.StartPosition, EndPosition = parsed.EndPosition,
                PageNumber = parsed.PageNumber, StyleName = parsed.StyleName,
                Type = classification.Type, Marker = classification.Marker,
                SectionId = stack.Count > 0 ? stack.Peek().Id : null
            };

            if (stack.Count > 0) stack.Peek().Paragraphs.Add(item);
            result.Paragraphs.Add(item);
        }

        void AddTable(ParsedTable table, ParsedBlock block)
        {
            var text = FormatTable(table);
            if (string.IsNullOrWhiteSpace(text)) return;

            var item = new DocumentParagraph
            {
                Order = block.Order, Text = text,
                StartPosition = block.StartPosition,
                EndPosition = block.EndPosition > block.StartPosition
                    ? block.EndPosition : block.StartPosition + text.Length,
                PageNumber = block.PageNumber, Type = ContentBlockType.Table,
                SectionId = stack.Count > 0 ? stack.Peek().Id : null
            };

            if (stack.Count > 0) stack.Peek().Paragraphs.Add(item);
            result.Paragraphs.Add(item);
        }

        var blocks = document.Blocks.OrderBy(x => x.Order).ToList();
        if (blocks.Count == 0)
        {
            foreach (var paragraph in paragraphs) AddParagraph(paragraph);
        }
        else
        {
            foreach (var block in blocks)
            {
                if (block.Kind == ParsedBlockKind.Paragraph
                    && block.ParagraphIndex >= 0
                    && block.ParagraphIndex < paragraphs.Count)
                    AddParagraph(paragraphs[block.ParagraphIndex]);
                else if (block.Kind == ParsedBlockKind.Table
                    && block.TableIndex >= 0
                    && block.TableIndex < document.Tables.Count)
                    AddTable(document.Tables[block.TableIndex], block);
            }
        }

        while (stack.Count > 0)
        {
            var section = stack.Pop();
            section.EndPosition = Math.Max(section.StartPosition, document.FullText.Length);
        }

        return result;
    }

    private static string FormatTable(ParsedTable table) =>
        string.Join(Environment.NewLine, table.Rows.Select((row, rowIndex) =>
            $"ردیف {rowIndex + 1}: " +
            string.Join(" | ", row.Cells.Select(cell =>
                $"ستون {cell.ColumnIndex + 1}: {cell.Text}"))));

    private static List<ParsedParagraph> FromPages(IReadOnlyCollection<ParsedPage> pages)
    {
        var output = new List<ParsedParagraph>();
        var globalOffset = 0;
        var order = 0;

        foreach (var page in pages.OrderBy(x => x.PageNumber))
        {
            var text = page.Text ?? string.Empty;
            var searchOffset = 0;

            foreach (var line in text.Split('\n'))
            {
                var clean = line.Trim();
                var index = clean.Length == 0 ? -1 :
                    text.IndexOf(clean, searchOffset, StringComparison.Ordinal);

                if (index >= 0)
                {
                    output.Add(new ParsedParagraph
                    {
                        Text = clean, Order = order++,
                        StartPosition = globalOffset + index,
                        EndPosition = globalOffset + index + clean.Length,
                        PageNumber = page.PageNumber
                    });
                    searchOffset = index + clean.Length;
                }
                else searchOffset = Math.Min(text.Length, searchOffset + line.Length + 1);
            }

            globalOffset += text.Length + 2;
        }

        return output;
    }
}