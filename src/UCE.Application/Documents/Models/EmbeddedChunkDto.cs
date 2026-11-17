namespace UCE.Application.Documents.Models;

/// <summary>
/// A source chunk paired with the vector generated from its embedding text.
/// The chunk remains unchanged so its source positions and citation metadata stay valid.
/// </summary>
public sealed class EmbeddedChunkDto
{
    public ChunkDto Chunk { get; set; } = new();
    public float[] Embedding { get; set; } = [];
}
