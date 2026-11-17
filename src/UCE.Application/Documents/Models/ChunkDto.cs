using UCE.Domain.Enums;

namespace UCE.Application.Documents.Models;

public sealed class ChunkDto
{
    public int Order { get; set; }
    public string Content { get; set; } = string.Empty;
    public int TokenCount { get; set; }
    public int CharacterCount => Content.Length;

    public Guid? SectionId { get; set; }
    public string? SectionTitle { get; set; }
    public int? SectionLevel { get; set; }
    public Guid? ParentSectionId { get; set; }
    public string? ParentSectionTitle { get; set; }

    /// <summary>
    /// Ordered section titles from the outermost non-root section to this chunk's section.
    /// </summary>
    public List<string> SectionPath { get; set; } = [];

    public int StartPosition { get; set; }
    public int EndPosition { get; set; }
    public int? StartPage { get; set; }
    public int? EndPage { get; set; }
    public ContentBlockType DominantBlockType { get; set; }
}
