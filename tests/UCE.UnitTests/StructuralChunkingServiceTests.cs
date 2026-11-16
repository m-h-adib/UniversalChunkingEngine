using UCE.Application.Documents.Models;
using UCE.Domain.Enums;
using UCE.Infrastructure.Chunking;
using Xunit;

namespace UCE.UnitTests;

public class StructuralChunkingServiceTests
{
    [Fact]
    public void KeepsEveryChunkWithinMaximumTokenLimitIncludingOverlap()
    {
        var service = new StructuralChunkingService(new ApproximateTokenCounter());
        var structure = new DocumentStructure();

        for (var i = 0; i < 20; i++)
        {
            structure.Paragraphs.Add(new DocumentParagraph
            {
                Order = i,
                Text = $"پاراگراف شماره {i} شامل چند واژه برای آزمایش اندازه چانک است.",
                StartPosition = i * 60,
                EndPosition = i * 60 + 58,
                Type = ContentBlockType.Paragraph
            });
        }

        var chunks = service.CreateChunks(structure, new ChunkingOptions
        {
            Strategy = ChunkingStrategy.Structural,
            MinChunkTokens = 10,
            TargetChunkTokens = 30,
            MaxChunkTokens = 40,
            OverlapTokens = 5
        });

        Assert.NotEmpty(chunks);
        Assert.All(chunks, chunk => Assert.True(chunk.TokenCount <= 40));
    }

    [Fact]
    public void RejectsUnsupportedSemanticStrategyInsteadOfSilentlyUsingStructural()
    {
        var service = new StructuralChunkingService(new ApproximateTokenCounter());

        Assert.Throws<NotSupportedException>(() =>
            service.CreateChunks(
                new DocumentStructure(),
                new ChunkingOptions { Strategy = ChunkingStrategy.Semantic }));
    }
}