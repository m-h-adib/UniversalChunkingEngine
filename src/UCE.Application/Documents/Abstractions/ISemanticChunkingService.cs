using UCE.Application.Documents.Models;

namespace UCE.Application.Documents.Abstractions;

public interface ISemanticChunkingService
{
    Task<IReadOnlyList<ChunkDto>> CreateChunksAsync(
        DocumentStructure structure,
        ChunkingOptions options,
        CancellationToken cancellationToken = default);
}
