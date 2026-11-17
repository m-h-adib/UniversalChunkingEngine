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
    [Fact]
    public void DoesNotCarryOverlapAcrossHeadingBoundaryWhenPreviousChunkWasAutoFlushed()
    {
        var service = new StructuralChunkingService(new ApproximateTokenCounter());
        var root = new DocumentStructureSection
        {
            Title = "متن اصلی سند",
            Level = 0
        };
        var chapter = new DocumentStructureSection
        {
            Title = "فصل اول: احرام",
            Level = 1,
            ParentId = root.Id
        };
        var subsection = new DocumentStructureSection
        {
            Title = "گفتار اول: میقات",
            Level = 2,
            ParentId = chapter.Id
        };
        root.Children.Add(chapter);
        chapter.Children.Add(subsection);

        var structure = new DocumentStructure();
        structure.Sections.Add(root);
        structure.Paragraphs.Add(new DocumentParagraph
        {
            Order = 0,
            Text = "این متن قبلی است و باید فقط در قطعه قبل از عنوان باقی بماند",
            StartPosition = 0,
            EndPosition = 60,
            SectionId = chapter.Id,
            Type = ContentBlockType.Paragraph
        });
        structure.Paragraphs.Add(new DocumentParagraph
        {
            Order = 1,
            Text = "گفتار اول: میقات",
            StartPosition = 62,
            EndPosition = 78,
            SectionId = subsection.Id,
            IsHeading = true,
            HeadingLevel = 2,
            Type = ContentBlockType.Heading
        });
        structure.Paragraphs.Add(new DocumentParagraph
        {
            Order = 2,
            Text = "شرح مربوط به این گفتار در این پاراگراف آمده است",
            StartPosition = 80,
            EndPosition = 128,
            SectionId = subsection.Id,
            Type = ContentBlockType.Paragraph
        });

        var chunks = service.CreateChunks(structure, new ChunkingOptions
        {
            Strategy = ChunkingStrategy.Structural,
            MinChunkTokens = 1,
            TargetChunkTokens = 8,
            MaxChunkTokens = 40,
            OverlapTokens = 5,
            PreserveHeadings = true
        });

        Assert.Equal(2, chunks.Count);
        Assert.StartsWith("گفتار اول: میقات", chunks[1].Content);
        Assert.DoesNotContain("این متن قبلی", chunks[1].Content);
        Assert.Equal(2, chunks[1].SectionLevel);
        Assert.Equal(chapter.Id, chunks[1].ParentSectionId);
        Assert.Equal("فصل اول: احرام", chunks[1].ParentSectionTitle);
        Assert.Equal(
            new[] { "فصل اول: احرام", "گفتار اول: میقات" },
            chunks[1].SectionPath);
    }

}
