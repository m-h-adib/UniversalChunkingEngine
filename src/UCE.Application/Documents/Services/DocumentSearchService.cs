using UCE.Application.Documents.Abstractions;
using UCE.Application.Documents.Models;

namespace UCE.Application.Documents.Services;

/// <summary>
/// Embeds a user's query with the same model used during indexing and searches Qdrant.
/// </summary>
public sealed class DocumentSearchService
{
    private readonly IEmbeddingGenerator _embeddingGenerator;
    private readonly IVectorStore _vectorStore;

    public DocumentSearchService(
        IEmbeddingGenerator embeddingGenerator,
        IVectorStore vectorStore)
    {
        _embeddingGenerator = embeddingGenerator;
        _vectorStore = vectorStore;
    }

    public async Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
        DocumentSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Query))
            throw new ArgumentException("Search query cannot be empty.", nameof(request));

        if (request.Limit is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(request), "Limit must be between 1 and 100.");

        var embeddings = await _embeddingGenerator.GenerateAsync(
            [request.Query.Trim()],
            cancellationToken);

        if (embeddings.Count != 1 || embeddings[0] is null || embeddings[0].Length == 0)
        {
            throw new InvalidOperationException(
                "The embedding service did not return exactly one non-empty query vector.");
        }

        return await _vectorStore.SearchAsync(
            embeddings[0],
            request.DocumentId,
            request.Limit,
            cancellationToken);
    }
}
