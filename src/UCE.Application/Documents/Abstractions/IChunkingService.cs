using UCE.Application.Documents.Models;
namespace UCE.Application.Documents.Abstractions;
public interface IChunkingService { IReadOnlyList<ChunkDto> CreateChunks(DocumentStructure structure, ChunkingOptions options); }