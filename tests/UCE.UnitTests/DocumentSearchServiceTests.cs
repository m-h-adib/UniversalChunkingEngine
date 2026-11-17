using UCE.Application.Documents.Abstractions;
using UCE.Application.Documents.Models;
using UCE.Application.Documents.Services;
using Xunit;

namespace UCE.UnitTests;

public sealed class DocumentSearchServiceTests
{
    [Fact]
    public async Task EmbedsQueryAndPassesDocumentFilterAndLimitToVectorStore()
    {
        var expected = new VectorSearchResult
        {
            Id = Guid.NewGuid(),
            DocumentId = Guid.NewGuid(),
            Content = "متن اصلی کتاب",
            ChunkOrder = 2,
            StartPosition = 20,
            EndPosition = 42,
            Score = 0.91
        };
        var embedding = new FakeEmbeddingGenerator();
        var store = new FakeVectorStore([expected]);
        var service = new DocumentSearchService(embedding, store);
        var documentId = Guid.NewGuid();

        var results = await service.SearchAsync(new DocumentSearchRequest
        {
            Query = "  موضوع کتاب چیست؟  ",
            DocumentId = documentId,
            Limit = 7
        });

        Assert.Equal("موضوع کتاب چیست؟", Assert.Single(embedding.ReceivedTexts));
        Assert.Equal(new[] { 0.25f, 0.5f }, store.ReceivedVector);
        Assert.Equal(documentId, store.ReceivedDocumentId);
        Assert.Equal(7, store.ReceivedLimit);
        Assert.Same(expected, Assert.Single(results));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RejectsEmptyQuery(string query)
    {
        var service = new DocumentSearchService(new FakeEmbeddingGenerator(), new FakeVectorStore([]));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.SearchAsync(new DocumentSearchRequest { Query = query }));
    }

    [Fact]
    public async Task RejectsLimitOutsideSupportedRange()
    {
        var service = new DocumentSearchService(new FakeEmbeddingGenerator(), new FakeVectorStore([]));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            service.SearchAsync(new DocumentSearchRequest { Query = "test", Limit = 101 }));
    }

    private sealed class FakeEmbeddingGenerator : IEmbeddingGenerator
    {
        public IReadOnlyList<string> ReceivedTexts { get; private set; } = [];

        public Task<IReadOnlyList<float[]>> GenerateAsync(
            IReadOnlyList<string> texts,
            CancellationToken cancellationToken = default)
        {
            ReceivedTexts = texts;
            return Task.FromResult<IReadOnlyList<float[]>>([[0.25f, 0.5f]]);
        }
    }

    private sealed class FakeVectorStore(IReadOnlyList<VectorSearchResult> results) : IVectorStore
    {
        public float[]? ReceivedVector { get; private set; }
        public Guid? ReceivedDocumentId { get; private set; }
        public int ReceivedLimit { get; private set; }

        public Task UpsertAsync(IReadOnlyList<VectorPoint> points, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
            float[] vector,
            Guid? documentId,
            int limit,
            CancellationToken cancellationToken = default)
        {
            ReceivedVector = vector;
            ReceivedDocumentId = documentId;
            ReceivedLimit = limit;
            return Task.FromResult(results);
        }
    }
}
