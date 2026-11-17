namespace UCE.Application.Documents.Models;

/// <summary>
/// A matching Qdrant point mapped to the original chunk and its citation metadata.
/// </summary>
public sealed class VectorSearchResult
{
    public Guid Id { get; init; }
    public double Score { get; init; }
    public Guid DocumentId { get; init; }
    public string? DocumentTitle { get; init; }
    public string? SourceFileName { get; init; }
    public string? Language { get; init; }
    public int ChunkOrder { get; init; }
    public string Content { get; init; } = string.Empty;
    public int TokenCount { get; init; }
    public int OverlapTokenCount { get; init; }
    public Guid? SectionId { get; init; }
    public string? SectionTitle { get; init; }
    public int? SectionLevel { get; init; }
    public int SectionDepth { get; init; }
    public Guid? ParentSectionId { get; init; }
    public string? ParentSectionTitle { get; init; }
    public IReadOnlyList<string> SectionPath { get; init; } = [];
    public int StartPosition { get; init; }
    public int EndPosition { get; init; }
    public int? StartPage { get; init; }
    public int? EndPage { get; init; }
    public string? DominantBlockType { get; init; }
}
