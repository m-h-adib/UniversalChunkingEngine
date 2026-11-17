using UCE.Application.Documents.Abstractions;
using UCE.Application.Documents.Models;
using UCE.Application.Documents.Services;
using UCE.Domain.Enums;
using Xunit;

namespace UCE.UnitTests;

public sealed class DocumentIndexingServiceTests
{
    [Fact]
    public async Task StoresOriginalChunkTextAndCitationMetadataAlongsideItsVector()
    {
        var chunk = new ChunkDto
        {
            Order = 3,
            Content = "متن دقیق و اصلی کتاب",
            TokenCount = 7,
            OverlapTokenCount = 0,
            SectionTitle = "فصل اول",
            SectionPath = ["فصل اول"],
            StartPosition = 120,
            EndPosition = 141,
            StartPage = 5,
            EndPage = 5
        };

        var store = new FakeVectorStore();
        var service = new DocumentIndexingService(
            new ChunkEmbeddingService(
                new FakeChunkingService([chunk]),
                new FakeEmbeddingGenerator([[0.1f, 0.2f, 0.3f]])),
            store);

        var result = await service.IndexAsync(
            new ParsedDocument
            {
                Title = "کتاب آزمایشی",
                SourceFileName = "book.docx",
                Language = DocumentLanguage.Persian
            },
            new DocumentStructure(),
            new ChunkingOptions());

        var point = Assert.Single(store.StoredPoints);
        Assert.NotEqual(Guid.Empty, result.DocumentId);
        Assert.Equal(result.DocumentId, (Guid)point.Payload.GetType()
            .GetProperty("documentId")!.GetValue(point.Payload)!);
        Assert.Equal("متن دقیق و اصلی کتاب", point.Payload.GetType()
            .GetProperty("content")!.GetValue(point.Payload));
        Assert.Equal(120, point.Payload.GetType()
            .GetProperty("startPosition")!.GetValue(point.Payload));
        Assert.Equal(141, point.Payload.GetType()
            .GetProperty("endPosition")!.GetValue(point.Payload));
        Assert.Equal(new[] { 0.1f, 0.2f, 0.3f }, point.Vector);
        Assert.Equal(1, result.ChunkCount);
        Assert.Equal(3, result.VectorDimensions);
    }

    [Fact]
    public async Task DoesNotCallVectorStoreWhenDocumentHasNoChunks()
    {
        var store = new FakeVectorStore();
        var service = new DocumentIndexingService(
            new ChunkEmbeddingService(
                new FakeChunkingService([]),
                new FakeEmbeddingGenerator([])),
            store);

        var result = await service.IndexAsync(
            new ParsedDocument(),
            new DocumentStructure(),
            new ChunkingOptions());

        Assert.Empty(store.StoredPoints);
        Assert.Equal(0, result.ChunkCount);
        Assert.Equal(0, result.VectorDimensions);
    }

    private sealed class FakeVectorStore : IVectorStore
    {
        public IReadOnlyList<VectorPoint> StoredPoints { get; private set; } = [];

        public Task UpsertAsync(
            IReadOnlyList<VectorPoint> points,
            CancellationToken cancellationToken = default)
        {
            StoredPoints = points;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeChunkingService(IReadOnlyList<ChunkDto> chunks) : IChunkingService
    {
        public IReadOnlyList<ChunkDto> CreateChunks(
            DocumentStructure structure,
            ChunkingOptions options) => chunks;
    }

    private sealed class FakeEmbeddingGenerator(IReadOnlyList<float[]> vectors) : IEmbeddingGenerator
    {
        public Task<IReadOnlyList<float[]>> GenerateAsync(
            IReadOnlyList<string> texts,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(vectors);
    }
}
