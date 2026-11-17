using UCE.Application.Documents.Abstractions;
using UCE.Application.Documents.Models;

namespace UCE.Application.Documents.Services;

/// <summary>
/// Coordinates structural chunking and vector generation without changing chunk content.
/// </summary>
public sealed class ChunkEmbeddingService
{
    private readonly IChunkingService _chunkingService;
    private readonly IEmbeddingGenerator _embeddingGenerator;

    public ChunkEmbeddingService(
        IChunkingService chunkingService,
        IEmbeddingGenerator embeddingGenerator)
    {
        _chunkingService = chunkingService;
        _embeddingGenerator = embeddingGenerator;
    }

    public async Task<IReadOnlyList<EmbeddedChunkDto>> CreateEmbeddedChunksAsync(
        DocumentStructure structure,
        ChunkingOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(structure);
        ArgumentNullException.ThrowIfNull(options);

        var chunks = _chunkingService.CreateChunks(structure, options);
        if (chunks.Count == 0)
        {
            return [];
        }

        var texts = chunks.Select(chunk => chunk.Content).ToArray();
        var embeddings = await _embeddingGenerator.GenerateAsync(
            texts,
            cancellationToken);

        if (embeddings.Count != chunks.Count)
        {
            throw new InvalidOperationException(
                $"Embedding count mismatch. Expected {chunks.Count}, received {embeddings.Count}.");
        }

        var result = new EmbeddedChunkDto[chunks.Count];
        for (var index = 0; index < chunks.Count; index++)
        {
            result[index] = new EmbeddedChunkDto
            {
                Chunk = chunks[index],
                Embedding = embeddings[index]
            };
        }

        return result;
    }
}
