using UCE.Application.Documents.Abstractions;
using UCE.Application.Documents.Models;

namespace UCE.Application.Documents.Services;

/// <summary>
/// Creates embeddings and persists them with the original source text and citation metadata.
/// Source chunk content and offsets are never rewritten.
/// </summary>
public sealed class DocumentIndexingService
{
    private readonly ChunkEmbeddingService _embeddingService;
    private readonly IVectorStore _vectorStore;

    public DocumentIndexingService(
        ChunkEmbeddingService embeddingService,
        IVectorStore vectorStore)
    {
        _embeddingService = embeddingService;
        _vectorStore = vectorStore;
    }

    public async Task<DocumentIndexingResult> IndexAsync(
        ParsedDocument document,
        DocumentStructure structure,
        ChunkingOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(structure);
        ArgumentNullException.ThrowIfNull(options);

        var embeddedChunks = await _embeddingService.CreateEmbeddedChunksAsync(
            structure,
            options,
            cancellationToken);

        var documentId = Guid.NewGuid();

        if (embeddedChunks.Count == 0)
        {
            return new DocumentIndexingResult
            {
                DocumentId = documentId,
                ChunkCount = 0,
                VectorDimensions = 0
            };
        }

        var points = embeddedChunks.Select(item => new VectorPoint
        {
            Id = Guid.NewGuid(),
            Vector = item.Embedding,
            Payload = new
            {
                documentId,
                documentTitle = document.Title,
                sourceFileName = document.SourceFileName,
                language = document.Language.ToString(),
                chunkOrder = item.Chunk.Order,
                content = item.Chunk.Content,
                tokenCount = item.Chunk.TokenCount,
                overlapTokenCount = item.Chunk.OverlapTokenCount,
                sectionId = item.Chunk.SectionId,
                sectionTitle = item.Chunk.SectionTitle,
                sectionLevel = item.Chunk.SectionLevel,
                sectionDepth = item.Chunk.SectionDepth,
                parentSectionId = item.Chunk.ParentSectionId,
                parentSectionTitle = item.Chunk.ParentSectionTitle,
                sectionPath = item.Chunk.SectionPath,
                startPosition = item.Chunk.StartPosition,
                endPosition = item.Chunk.EndPosition,
                startPage = item.Chunk.StartPage,
                endPage = item.Chunk.EndPage,
                dominantBlockType = item.Chunk.DominantBlockType.ToString()
            }
        }).ToArray();

        await _vectorStore.UpsertAsync(points, cancellationToken);

        return new DocumentIndexingResult
        {
            DocumentId = documentId,
            ChunkCount = points.Length,
            VectorDimensions = points[0].Vector.Length
        };
    }
}
