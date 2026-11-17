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
            "موضوع اول. توضیح اول.\n\nادامه همان موضوع با توضیح تکمیلی.",
            chunks[0].Content);
        Assert.Equal("موضوع مستقل دوم و توضیح آن.", chunks[1].Content);
        Assert.Equal(0, chunks[0].StartPosition);
        Assert.Equal(structure.Paragraphs[1].EndPosition, chunks[0].EndPosition);
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

    private sealed class FakeBoundaryDetector(IReadOnlySet<int> boundaries)
        : ISemanticBoundaryDetector
    {
        public Task<IReadOnlySet<int>> DetectBoundariesAsync(
            IReadOnlyList<SemanticParagraphInput> paragraphs,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(boundaries);
    }
}
