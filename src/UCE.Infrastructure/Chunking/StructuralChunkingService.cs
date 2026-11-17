using System.Text.RegularExpressions;
using UCE.Application.Documents.Abstractions;
using UCE.Application.Documents.Models;
using UCE.Domain.Enums;

namespace UCE.Infrastructure.Chunking;

public sealed class StructuralChunkingService(ITokenCounter counter) : IChunkingService
{
    private static readonly Regex TokenPattern =
        new(@"\w+|[^\s]", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public IReadOnlyList<ChunkDto> CreateChunks(
        DocumentStructure structure,
        ChunkingOptions options)
    {
        ArgumentNullException.ThrowIfNull(structure);
        ArgumentNullException.ThrowIfNull(options);

        if (options.MaxChunkTokens <= 0
            || options.TargetChunkTokens <= 0
            || options.MinChunkTokens < 0
            || options.OverlapTokens < 0
            || options.TargetChunkTokens > options.MaxChunkTokens
            || options.MinChunkTokens > options.MaxChunkTokens)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options), "Chunk token limits are inconsistent.");
        }

        if (options.Strategy is ChunkingStrategy.Semantic or ChunkingStrategy.Hybrid)
        {
            throw new NotSupportedException(
                $"Chunking strategy '{options.Strategy}' is not implemented. " +
                "Use Structural or Adaptive.");
        }

        // Adaptive currently uses the structural baseline until an adaptive planner is added.
        var result = new List<ChunkDto>();
        var current = new List<DocumentParagraph>();
        var currentTokens = 0;
        var overlapText = string.Empty;

        void Flush()
        {
            if (current.Count == 0) return;

            var body = string.Join(
                Environment.NewLine + Environment.NewLine,
                current.Select(x => x.Text));

            var content = string.IsNullOrWhiteSpace(overlapText)
                ? body
                : overlapText + Environment.NewLine + Environment.NewLine + body;

            var pages = current.Where(x => x.PageNumber.HasValue)
                .Select(x => x.PageNumber!.Value).ToList();

            var sectionId = current[0].SectionId;
            var sectionPath = FindSectionPath(structure.Sections, sectionId)
                .Where(x => x.Level > 0)
                .ToList();
            var section = sectionPath.LastOrDefault();
            var parentSection = sectionPath.Count > 1
                ? sectionPath[^2]
                : null;

            result.Add(new ChunkDto
            {
                Order = result.Count,
                Content = content,
                TokenCount = counter.Count(content),
                SectionId = sectionId,
                SectionTitle = section?.Title
                    ?? FindSectionTitle(structure.Sections, sectionId),
                SectionLevel = section?.Level,
                SectionDepth = sectionPath.Count,
                ParentSectionId = parentSection?.Id,
                ParentSectionTitle = parentSection?.Title,
                SectionPath = sectionPath.Select(x => x.Title).ToList(),
                StartPosition = current.Min(x => x.StartPosition),
                EndPosition = current.Max(x => x.EndPosition),
                StartPage = pages.Count > 0 ? pages.Min() : null,
                EndPage = pages.Count > 0 ? pages.Max() : null,
                DominantBlockType = current.GroupBy(x => x.Type)
                    .OrderByDescending(x => x.Count()).First().Key
            });

            overlapText = options.OverlapTokens > 0
                ? TakeTailTokens(body, options.OverlapTokens)
                : string.Empty;

            current.Clear();
            currentTokens = 0;
        }

        foreach (var original in structure.Paragraphs.OrderBy(x => x.Order))
        {
            if (string.IsNullOrWhiteSpace(original.Text)) continue;

            foreach (var paragraph in SplitIfRequired(original, options))
            {
                var paragraphTokens = counter.Count(paragraph.Text);

                // A heading starts a new structural context. Clear overlap even when
                // the previous chunk was already flushed by the token target.
                if (options.PreserveHeadings && paragraph.IsHeading)
                {
                    if (current.Count > 0)
                        Flush();

                    overlapText = string.Empty;
                }

                TrimOverlapToFit(
                    paragraphTokens, options.MaxChunkTokens, ref overlapText);

                var projected = currentTokens
                    + counter.Count(overlapText)
                    + paragraphTokens;

                if (current.Count > 0 && projected > options.MaxChunkTokens)
                {
                    Flush();
                    TrimOverlapToFit(
                        paragraphTokens, options.MaxChunkTokens, ref overlapText);
                }

                current.Add(paragraph);
                currentTokens += paragraphTokens;

                var actualTokens = currentTokens + counter.Count(overlapText);
                if (actualTokens >= options.TargetChunkTokens
                    && currentTokens >= options.MinChunkTokens)
                {
                    Flush();
                }
            }
        }

        Flush();
        return result;
    }

    private List<DocumentParagraph> SplitIfRequired(
        DocumentParagraph paragraph,
        ChunkingOptions options)
    {
        var text = paragraph.Text;
        var splitLimit = options.PreserveParagraphs
            ? options.MaxChunkTokens
            : options.TargetChunkTokens;

        if (counter.Count(text) <= splitLimit)
            return [paragraph];

        var words = Regex.Matches(text, @"\S+");
        var pieces = new List<DocumentParagraph>();
        var buffer = new List<string>();
        var bufferStart = 0;
        var bufferEnd = 0;
        var pieceIndex = 0;

        void AddWord(string value, int start, int end)
        {
            if (buffer.Count == 0)
                bufferStart = start;

            buffer.Add(value);
            bufferEnd = end;
        }

        void AddPiece()
        {
            if (buffer.Count == 0) return;

            var value = string.Join(" ", buffer);
            pieces.Add(new DocumentParagraph
            {
                Order = paragraph.Order,
                Text = value,
                StartPosition = paragraph.StartPosition + bufferStart,
                EndPosition = paragraph.StartPosition + bufferEnd,
                PageNumber = paragraph.PageNumber,
                IsHeading = paragraph.IsHeading && pieceIndex == 0,
                HeadingLevel = paragraph.HeadingLevel,
                StyleName = paragraph.StyleName,
                Type = paragraph.Type,
                Marker = paragraph.Marker,
                SectionId = paragraph.SectionId
            });

            pieceIndex++;
            buffer.Clear();
        }

        foreach (Match word in words)
        {
            var candidate = buffer.Count == 0
                ? word.Value
                : string.Join(" ", buffer) + " " + word.Value;

            if (buffer.Count > 0 && counter.Count(candidate) > splitLimit)
            {
                AddPiece();
                AddWord(word.Value, word.Index, word.Index + word.Length);
                continue;
            }

            if (buffer.Count == 0 && counter.Count(word.Value) > splitLimit)
            {
                var remainder = word.Value;
                var offset = 0;

                while (remainder.Length > 0)
                {
                    var length = remainder.Length;
                    while (length > 1 && counter.Count(remainder[..length]) > splitLimit)
                        length /= 2;

                    var part = remainder[..length];
                    AddWord(part, word.Index + offset, word.Index + offset + length);
                    AddPiece();
                    remainder = remainder[length..];
                    offset += length;
                }

                continue;
            }

            AddWord(word.Value, word.Index, word.Index + word.Length);
        }

        AddPiece();
        return pieces.Count > 0 ? pieces : [paragraph];
    }

    private void TrimOverlapToFit(
        int nextParagraphTokens,
        int maxTokens,
        ref string overlap)
    {
        if (string.IsNullOrEmpty(overlap)) return;

        var available = Math.Max(0, maxTokens - nextParagraphTokens);
        if (counter.Count(overlap) <= available) return;

        overlap = TakeTailTokens(overlap, available);
    }

    private string TakeTailTokens(string text, int tokenCount)
    {
        if (tokenCount <= 0 || string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var matches = TokenPattern.Matches(text);
        if (matches.Count == 0) return string.Empty;

        var start = Math.Max(0, matches.Count - tokenCount);
        var suffix = text[matches[start].Index..].Trim();

        while (start < matches.Count - 1 && counter.Count(suffix) > tokenCount)
        {
            start++;
            suffix = text[matches[start].Index..].Trim();
        }

        while (start > 0)
        {
            var candidate = text[matches[start - 1].Index..].Trim();
            if (counter.Count(candidate) > tokenCount) break;
            suffix = candidate;
            start--;
        }

        return suffix;
    }

    private static List<DocumentStructureSection> FindSectionPath(
        IEnumerable<DocumentStructureSection> sections,
        Guid? id)
    {
        if (!id.HasValue)
            return [];

        foreach (var section in sections)
        {
            if (section.Id == id.Value)
                return [section];

            var childPath = FindSectionPath(section.Children, id);
            if (childPath.Count > 0)
            {
                childPath.Insert(0, section);
                return childPath;
            }
        }

        return [];
    }

    private static string? FindSectionTitle(
        IEnumerable<DocumentStructureSection> sections,
        Guid? id)
    {
        foreach (var section in sections)
        {
            if (section.Id == id) return section.Title;
            var child = FindSectionTitle(section.Children, id);
            if (child is not null) return child;
        }

        return null;
    }
}
