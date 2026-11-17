namespace UCE.Application.Documents.Models;

public sealed class DocumentIndexingResult
{
    public Guid DocumentId { get; init; }
    public int ChunkCount { get; init; }
    public int VectorDimensions { get; init; }
}
