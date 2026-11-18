using Microsoft.Extensions.Options;
using UCE.Application.Documents.Abstractions;
using UCE.Application.Documents.Models;
using UCE.Domain.Enums;
using UCE.Infrastructure.Chunking;
using Xunit;

namespace UCE.UnitTests;

public sealed class SemanticChunkingServiceTests
{
    [Fact]
    public async Task SplitsAtLlmBoundaryAndPreservesOriginalParagraphText()
    {
        var counter = new ApproximateTokenCounter();
        var detector = new FakeBoundaryDetector(new HashSet<int> { 1 });
        var service = new SemanticChunkingService(
            detector,
            new StructuralChunkingService(counter),
            counter,
            Options.Create(new SemanticChunkingOptions()));

        var structure = new DocumentStructure();
        structure.Paragraphs.Add(Paragraph(0, "موضوع اول. توضیح اول.", 0));
        structure.Paragraphs.Add(Paragraph(1, "ادامه همان موضوع با توضیح تکمیلی.", 24));
        structure.Paragraphs.Add(Paragraph(2, "موضوع مستقل دوم و توضیح آن.", 65));

        var chunks = await service.CreateChunksAsync(structure, new ChunkingOptions
        {
            Strategy = ChunkingStrategy.Semantic,
            MinChunkTokens = 1,
            TargetChunkTokens = 100,
            MaxChunkTokens = 200,
            OverlapTokens = 0
        });

        Assert.Equal(2, chunks.Count);
        Assert.Equal(
            $"موضوع اول. توضیح اول.{Environment.NewLine}{Environment.NewLine}ادامه همان موضوع با توضیح تکمیلی.",
            chunks[0].Content);
        Assert.Equal("موضوع مستقل دوم و توضیح آن.", chunks[1].Content);
        Assert.Equal(0, chunks[0].StartPosition);
        Assert.Equal(structure.Paragraphs[1].EndPosition, chunks[0].EndPosition);
    }


    [Fact]
    public async Task SemanticCanKeepRelatedParagraphsAcrossStructuralSections()
    {
        var counter = new ApproximateTokenCounter();
        var service = new SemanticChunkingService(
            new FakeBoundaryDetector(new HashSet<int>()),
            new StructuralChunkingService(counter),
            counter,
            Options.Create(new SemanticChunkingOptions()));
        var structure = new DocumentStructure();
        structure.Paragraphs.Add(new DocumentParagraph
        {
            Order = 0,
            Text = "آغاز یک موضوع",
            StartPosition = 0,
            EndPosition = 14,
            SectionId = Guid.NewGuid()
        });
        structure.Paragraphs.Add(new DocumentParagraph
        {
            Order = 1,
            Text = "ادامه همان موضوع در بخش بعدی",
            StartPosition = 16,
            EndPosition = 45,
            SectionId = Guid.NewGuid()
        });

        var chunks = await service.CreateChunksAsync(structure, new ChunkingOptions
        {
            Strategy = ChunkingStrategy.Semantic,
            MinChunkTokens = 1,
            TargetChunkTokens = 100,
            MaxChunkTokens = 200,
            OverlapTokens = 0
        });

        Assert.Single(chunks);
        Assert.Contains("آغاز یک موضوع", chunks[0].Content);
        Assert.Contains("ادامه همان موضوع در بخش بعدی", chunks[0].Content);
    }

    [Fact]
    public async Task SemanticDetectorReceivesCompleteLongParagraphsWithoutTruncation()
    {
        var counter = new ApproximateTokenCounter();
        var detector = new RecordingBoundaryDetector();
        var longText = new string('الف', 500);
        var options = new SemanticChunkingOptions
        {
            MaxParagraphsPerRequest = 2,
            MaxInputCharacters = 30
        };
        var service = new SemanticChunkingService(
            detector,
            new StructuralChunkingService(counter),
            counter,
            Options.Create(options));
        var structure = new DocumentStructure();
        structure.Paragraphs.Add(Paragraph(0, longText, 0));
        structure.Paragraphs.Add(Paragraph(1, "پاراگراف دوم", 501));
        structure.Paragraphs.Add(Paragraph(2, "پاراگراف سوم", 514));

        await service.CreateChunksAsync(structure, new ChunkingOptions
        {
            Strategy = ChunkingStrategy.Semantic,
            MinChunkTokens = 1,
            TargetChunkTokens = 1000,
            MaxChunkTokens = 1200,
            OverlapTokens = 0
        });

        Assert.NotEmpty(detector.Batches);
        Assert.Equal(longText, detector.Batches[0][0].Text);
    }

    [Fact]
    public async Task HybridKeepsStructuralBoundariesEvenWhenLlmFindsNoTopicChange()
    {
        var counter = new ApproximateTokenCounter();
        var service = new SemanticChunkingService(
            new FakeBoundaryDetector(new HashSet<int>()),
            new StructuralChunkingService(counter),
            counter,
            Options.Create(new SemanticChunkingOptions()));
        var structure = new DocumentStructure();
        structure.Paragraphs.Add(Paragraph(0, "این متن نخست است", 0));
        structure.Paragraphs.Add(Paragraph(1, "این متن دوم است", 20));

        var chunks = await service.CreateChunksAsync(structure, new ChunkingOptions
        {
            Strategy = ChunkingStrategy.Hybrid,
            MinChunkTokens = 1,
            TargetChunkTokens = 4,
            MaxChunkTokens = 20,
            OverlapTokens = 0
        });

        Assert.Equal(2, chunks.Count);
        Assert.Equal("این متن نخست است", chunks[0].Content);
        Assert.Equal("این متن دوم است", chunks[1].Content);
    }

    private static DocumentParagraph Paragraph(int order, string text, int start) => new()
    {
        Order = order,
        Text = text,
        StartPosition = start,
        EndPosition = start + text.Length,
        Type = ContentBlockType.Paragraph
    };

    private sealed class RecordingBoundaryDetector : ISemanticBoundaryDetector
    {
        public List<IReadOnlyList<SemanticParagraphInput>> Batches { get; } = [];

        public Task<IReadOnlySet<int>> DetectBoundariesAsync(
            IReadOnlyList<SemanticParagraphInput> paragraphs,
            CancellationToken cancellationToken = default)
        {
            Batches.Add(paragraphs.ToArray());
            return Task.FromResult<IReadOnlySet<int>>(new HashSet<int>());
        }
    }

    private sealed class FakeBoundaryDetector(IReadOnlySet<int> boundaries)
        : ISemanticBoundaryDetector
    {
        public Task<IReadOnlySet<int>> DetectBoundariesAsync(
            IReadOnlyList<SemanticParagraphInput> paragraphs,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(boundaries);
    }
}
