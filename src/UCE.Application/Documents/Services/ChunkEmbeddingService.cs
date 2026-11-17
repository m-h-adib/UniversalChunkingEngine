using UCE.Application.Documents.Abstractions;
using UCE.Application.Documents.Models;
using UCE.Domain.Enums;

namespace UCE.Application.Documents.Services;

/// <summary>
/// Coordinates structural chunking and vector generation without changing chunk content.
/// </summary>
public sealed class ChunkEmbeddingService
{
    private readonly IChunkingService _chunkingService;
    private readonly IEmbeddingGenerator _embeddingGenerator;
    private readonly ISemanticChunkingService? _semanticChunkingService;

    public ChunkEmbeddingService(
        IChunkingService chunkingService,
        IEmbeddingGenerator embeddingGenerator,
        ISemanticChunkingService? semanticChunkingService = null)
    {
        _chunkingService = chunkingService;
        _embeddingGenerator = embeddingGenerator;
        _semanticChunkingService = semanticChunkingService;
    }

    public async Task<IReadOnlyList<EmbeddedChunkDto>> CreateEmbeddedChunksAsync(
        DocumentStructure structure,
        ChunkingOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(structure);
        ArgumentNullException.ThrowIfNull(options);

        IReadOnlyList<ChunkDto> chunks;
        if (options.Strategy is ChunkingStrategy.Semantic or ChunkingStrategy.Hybrid)
        {
            if (_semanticChunkingService is null)
                throw new InvalidOperationException("Semantic chunking service is not registered.");
            chunks = await _semanticChunkingService.CreateChunksAsync(
                structure, options, cancellationToken);
        }
        else
        {
            chunks = _chunkingService.CreateChunks(structure, options);
        }
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
