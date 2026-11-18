using System.Text.RegularExpressions;
using UCE.Application.Documents.Abstractions;
using UCE.Application.Documents.Models;
using UCE.Domain.Enums;
using Microsoft.Extensions.Options;

namespace UCE.Infrastructure.Chunking;

/// <summary>
/// Plans semantic boundaries with an LLM, then builds chunks from the untouched
/// source paragraphs. Structural section and heading boundaries are always hard limits.
/// Hybrid additionally retains the boundaries produced by the structural chunker.
/// </summary>
public sealed class SemanticChunkingService(
    ISemanticBoundaryDetector boundaryDetector,
    IChunkingService structuralChunker,
    ITokenCounter tokenCounter,
    IOptions<SemanticChunkingOptions> semanticOptions) : ISemanticChunkingService
{
    private readonly SemanticChunkingOptions _semanticOptions = semanticOptions.Value;
    private static readonly Regex Words = new(@"\S+", RegexOptions.Compiled);

    public async Task<IReadOnlyList<ChunkDto>> CreateChunksAsync(
        DocumentStructure structure,
        ChunkingOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(structure);
        ArgumentNullException.ThrowIfNull(options);
        Validate(options);

        var paragraphs = structure.Paragraphs
            .Where(x => !string.IsNullOrWhiteSpace(x.Text))
            .OrderBy(x => x.Order)
            .ToList();

        if (paragraphs.Count == 0)
            return [];

        var semanticBoundaries = await DetectAllBoundariesAsync(
            structure, paragraphs, cancellationToken);

        var structuralOptions = new ChunkingOptions
        {
            Strategy = ChunkingStrategy.Structural,
            MinChunkTokens = options.MinChunkTokens,
            TargetChunkTokens = options.TargetChunkTokens,
            MaxChunkTokens = options.MaxChunkTokens,
            OverlapTokens = options.OverlapTokens,
            PreserveHeadings = options.PreserveHeadings,
            PreserveParagraphs = options.PreserveParagraphs
        };
        var structuralEnds = options.Strategy == ChunkingStrategy.Hybrid
            ? structuralChunker.CreateChunks(structure, structuralOptions)
                .Select(x => x.EndPosition).ToHashSet()
            : new HashSet<int>();

        var chunks = new List<ChunkDto>();
        var current = new List<DocumentParagraph>();
        var currentTokens = 0;
        string overlap = string.Empty;
        Guid? activeSectionId = null;
        var sectionActive = false;

        void Flush()
        {
            if (current.Count == 0)
                return;

            if (current.All(x => x.IsHeading))
            {
                current.Clear();
                currentTokens = 0;
                overlap = string.Empty;
                return;
            }

            var body = string.Join(
                Environment.NewLine + Environment.NewLine,
                current.Select(x => x.Text));

            var content = string.IsNullOrWhiteSpace(overlap)
                ? body
                : overlap + Environment.NewLine + Environment.NewLine + body;

            var first = current[0];
            var sectionPath = FindSectionPath(structure.Sections, first.SectionId)
                .Where(x => x.Level > 0).ToList();
            var section = sectionPath.LastOrDefault();
            var parent = sectionPath.Count > 1 ? sectionPath[^2] : null;
            var pages = current.Where(x => x.PageNumber.HasValue)
                .Select(x => x.PageNumber!.Value).ToList();

            chunks.Add(new ChunkDto
            {
                Order = chunks.Count,
                Content = content,
                TokenCount = tokenCounter.Count(content),
                OverlapTokenCount = tokenCounter.Count(overlap),
                SectionId = first.SectionId,
                SectionTitle = section?.Title,
                SectionLevel = section?.Level,
                SectionDepth = sectionPath.Count,
                ParentSectionId = parent?.Id,
                ParentSectionTitle = parent?.Title,
                SectionPath = sectionPath.Select(x => x.Title).ToList(),
                StartPosition = current.Min(x => x.StartPosition),
                EndPosition = current.Max(x => x.EndPosition),
                StartPage = pages.Count == 0 ? null : pages.Min(),
                EndPage = pages.Count == 0 ? null : pages.Max(),
                DominantBlockType = current.GroupBy(x => x.Type)
                    .OrderByDescending(x => x.Count()).First().Key
            });

            overlap = options.OverlapTokens > 0
                ? TakeTail(body, options.OverlapTokens)
                : string.Empty;
            current.Clear();
            currentTokens = 0;
        }

        for (var index = 0; index < paragraphs.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var paragraph = paragraphs[index];

            var hardBoundary = options.Strategy == ChunkingStrategy.Hybrid
                && sectionActive
                && (paragraph.SectionId != activeSectionId
                    || (options.PreserveHeadings && paragraph.IsHeading));

            if (hardBoundary && current.Count > 0)
                Flush();

            if (hardBoundary)
                overlap = string.Empty;

            activeSectionId = paragraph.SectionId;
            sectionActive = true;

            foreach (var piece in SplitOversized(paragraph, options.MaxChunkTokens))
            {
                var pieceTokens = tokenCounter.Count(piece.Text);
                if (tokenCounter.Count(overlap) + pieceTokens > options.MaxChunkTokens)
                    overlap = TakeTail(overlap, Math.Max(0, options.MaxChunkTokens - pieceTokens));

                if (current.Count > 0
                    && currentTokens + tokenCounter.Count(overlap) + pieceTokens > options.MaxChunkTokens)
                {
                    Flush();
                    overlap = TakeTail(overlap, Math.Max(0, options.MaxChunkTokens - pieceTokens));
                }

                current.Add(piece);
                currentTokens += pieceTokens;

                var isLastPiece = piece.EndPosition == paragraph.EndPosition;
                var semanticBoundary = isLastPiece && semanticBoundaries.Contains(index);
                var structuralBoundary = isLastPiece && structuralEnds.Contains(paragraph.EndPosition);

                if (semanticBoundary || structuralBoundary)
                    Flush();
            }
        }

        Flush();
        return chunks;
    }

    private async Task<HashSet<int>> DetectAllBoundariesAsync(
        DocumentStructure structure,
        IReadOnlyList<DocumentParagraph> paragraphs,
        CancellationToken cancellationToken)
    {
        var result = new HashSet<int>();
        var start = 0;
        var maxParagraphs = Math.Max(2, _semanticOptions.MaxParagraphsPerRequest);
        var maxCharacters = Math.Max(1, _semanticOptions.MaxInputCharacters);

        // Semantic analysis follows document order, not SectionId. Section metadata
        // is still supplied to the model, but structural section transitions do not
        // prevent related paragraphs from being evaluated together.
        while (start < paragraphs.Count - 1)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var end = start;
            var characterCount = paragraphs[start].Text.Length;

            // Keep one paragraph of overlap between requests. Never truncate source
            // paragraphs: the character limit is a batching target, not a text limit.
            while (end + 1 < paragraphs.Count
                && end - start + 1 < maxParagraphs)
            {
                var nextLength = paragraphs[end + 1].Text.Length;
                var hasRightContext = end > start;
                if (hasRightContext && characterCount + nextLength > maxCharacters)
                    break;

                characterCount += nextLength;
                end++;
            }

            var batch = paragraphs.Skip(start).Take(end - start + 1)
                .Select(x => new SemanticParagraphInput(
                    x.Order,
                    x.Text,
                    x.IsHeading,
                    FindSectionPath(structure.Sections, x.SectionId)
                        .LastOrDefault()?.Title))
                .ToArray();

            var boundaries = await boundaryDetector.DetectBoundariesAsync(
                batch, cancellationToken);

            foreach (var localIndex in boundaries)
                result.Add(start + localIndex);

            // The final paragraph is repeated as the first paragraph of the next
            // batch so a topic transition at a batch edge still has right context.
            start = end;
        }

        return result;
    }

    private IEnumerable<DocumentParagraph> SplitOversized(
        DocumentParagraph paragraph,
        int maxTokens)
    {
        if (tokenCounter.Count(paragraph.Text) <= maxTokens)
            return [paragraph];

        var pieces = new List<DocumentParagraph>();

        void AddPiece(int start, int end)
        {
            if (end <= start) return;

            pieces.Add(new DocumentParagraph
            {
                Order = paragraph.Order,
                Text = paragraph.Text[start..end],
                StartPosition = paragraph.StartPosition + start,
                EndPosition = paragraph.StartPosition + end,
                PageNumber = paragraph.PageNumber,
                IsHeading = paragraph.IsHeading && pieces.Count == 0,
                HeadingLevel = paragraph.HeadingLevel,
                StyleName = paragraph.StyleName,
                Type = paragraph.Type,
                Marker = paragraph.Marker,
                SectionId = paragraph.SectionId
            });
        }

        void SplitOversizedSentence(int start, int end)
        {
            var sentence = paragraph.Text[start..end];
            var words = Regex.Matches(sentence, @"\\S+");
            if (words.Count == 0)
            {
                AddPiece(start, end);
                return;
            }

            var pieceStart = start;
            var hasWord = false;

            foreach (Match word in words)
            {
                var wordStart = start + word.Index;
                var wordEnd = wordStart + word.Length;
                var candidate = paragraph.Text[pieceStart..wordEnd];

                if (hasWord && tokenCounter.Count(candidate) > maxTokens)
                {
                    // Prefer a word boundary only when a single sentence itself
                    // cannot fit. Whitespace remains attached to the preceding piece.
                    AddPiece(pieceStart, wordStart);
                    pieceStart = wordStart;
                    hasWord = false;
                }

                hasWord = true;
            }

            AddPiece(pieceStart, end);
        }

        var sentenceEnds = Regex.Matches(
                paragraph.Text, @"(?<=[.!?؟؛])(?=\\s|$)")
            .Select(x => x.Index)
            .Where(x => x > 0 && x < paragraph.Text.Length)
            .Distinct()
            .OrderBy(x => x)
            .ToList();

        var ranges = new List<(int Start, int End)>();
        var cursor = 0;
        foreach (var sentenceEnd in sentenceEnds)
        {
            ranges.Add((cursor, sentenceEnd));
            cursor = sentenceEnd;
        }
        if (cursor < paragraph.Text.Length || ranges.Count == 0)
            ranges.Add((cursor, paragraph.Text.Length));

        var pendingStart = -1;
        var pendingEnd = -1;

        foreach (var range in ranges)
        {
            var candidateStart = pendingStart >= 0 ? pendingStart : range.Start;
            if (pendingStart >= 0
                && tokenCounter.Count(paragraph.Text[candidateStart..range.End]) > maxTokens)
            {
                AddPiece(pendingStart, pendingEnd);
                pendingStart = -1;
                pendingEnd = -1;
            }

            if (pendingStart < 0)
            {
                if (tokenCounter.Count(paragraph.Text[range.Start..range.End]) > maxTokens)
                {
                    SplitOversizedSentence(range.Start, range.End);
                    continue;
                }

                pendingStart = range.Start;
            }

            pendingEnd = range.End;
        }

        if (pendingStart >= 0)
            AddPiece(pendingStart, pendingEnd);

        return pieces.Count == 0 ? [paragraph] : pieces;
    }

    private string TakeTail(string text, int tokenLimit)
    {
        if (tokenLimit <= 0) return string.Empty;
        var matches = Regex.Matches(text, @"\w+|[^\s]");
        if (matches.Count <= tokenLimit) return text;
        return text[matches[matches.Count - tokenLimit].Index..].Trim();
    }

    private static List<DocumentStructureSection> FindSectionPath(
        IEnumerable<DocumentStructureSection> sections, Guid? id)
    {
        if (!id.HasValue) return [];
        foreach (var section in sections)
        {
            if (section.Id == id.Value) return [section];
            var child = FindSectionPath(section.Children, id);
            if (child.Count > 0) { child.Insert(0, section); return child; }
        }
        return [];
    }

    private static void Validate(ChunkingOptions options)
    {
        if (options.MaxChunkTokens <= 0 || options.TargetChunkTokens <= 0
            || options.MinChunkTokens < 0 || options.OverlapTokens < 0
            || options.TargetChunkTokens > options.MaxChunkTokens
            || options.MinChunkTokens > options.MaxChunkTokens)
            throw new ArgumentOutOfRangeException(nameof(options), "Chunk token limits are inconsistent.");
    }
}
