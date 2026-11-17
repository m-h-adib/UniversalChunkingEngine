using UCE.Application.Documents.Abstractions;
using UCE.Application.Documents.Models;
using UCE.Application.Documents.Services;
using Xunit;

namespace UCE.UnitTests;

public sealed class ChunkEmbeddingServiceTests
{
    [Fact]
    public async Task GeneratesVectorsFromChunkContentAndPreservesOriginalChunkMetadata()
    {
        var chunk = new ChunkDto
        {
            Order = 4,
            Content = "متن اصلی چانک",
            StartPosition = 120,
            EndPosition = 145,
            SectionTitle = "فصل آزمایشی"
        };
        var chunker = new FakeChunkingService([chunk]);
        var generator = new FakeEmbeddingGenerator([[0.1f, 0.2f]]);
        var service = new ChunkEmbeddingService(chunker, generator);

        var result = await service.CreateEmbeddedChunksAsync(
            new DocumentStructure(),
            new ChunkingOptions());

        var embedded = Assert.Single(result);
        Assert.Same(chunk, embedded.Chunk);
        Assert.Equal("متن اصلی چانک", Assert.Single(generator.ReceivedTexts));
        Assert.Equal(120, embedded.Chunk.StartPosition);
        Assert.Equal(145, embedded.Chunk.EndPosition);
        Assert.Equal(new[] { 0.1f, 0.2f }, embedded.Embedding);
    }

    [Fact]
    public async Task ReturnsEmptyResultWithoutCallingEmbeddingServiceWhenThereAreNoChunks()
    {
        var generator = new FakeEmbeddingGenerator([]);
        var service = new ChunkEmbeddingService(
            new FakeChunkingService([]),
            generator);

        var result = await service.CreateEmbeddedChunksAsync(
            new DocumentStructure(),
            new ChunkingOptions());

        Assert.Empty(result);
        Assert.Empty(generator.ReceivedTexts);
    }

    [Fact]
    public async Task ThrowsWhenEmbeddingCountDoesNotMatchChunkCount()
    {
        var service = new ChunkEmbeddingService(
            new FakeChunkingService([new ChunkDto { Content = "متن" }]),
            new FakeEmbeddingGenerator([]));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateEmbeddedChunksAsync(
                new DocumentStructure(),
                new ChunkingOptions()));
    }

    private sealed class FakeChunkingService(IReadOnlyList<ChunkDto> chunks) : IChunkingService
    {
        public IReadOnlyList<ChunkDto> CreateChunks(
            DocumentStructure structure,
            ChunkingOptions options) => chunks;
    }

    private sealed class FakeEmbeddingGenerator(IReadOnlyList<float[]> vectors) : IEmbeddingGenerator
    {
        public IReadOnlyList<string> ReceivedTexts { get; private set; } = [];

        public Task<IReadOnlyList<float[]>> GenerateAsync(
            IReadOnlyList<string> texts,
            CancellationToken cancellationToken = default)
        {
            ReceivedTexts = texts;
            return Task.FromResult(vectors);
        }
    }
}
