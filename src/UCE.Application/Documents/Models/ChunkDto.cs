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

    /// <summary>
    /// Logical heading level detected from the source document. It may differ from
    /// SectionDepth when the document skips a heading level.
    /// </summary>
    public int? SectionLevel { get; set; }

    /// <summary>
    /// Actual number of non-root sections in SectionPath.
    /// </summary>
    public int SectionDepth { get; set; }

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

    /// <summary>
    /// Most frequent ContentBlockType among source paragraphs included in this chunk.
    /// This describes source block classification, not the semantic role of every
    /// sentence inside Content.
    /// </summary>
    public ContentBlockType DominantBlockType { get; set; }
}
