namespace UCE.Application.Documents.Models;

/// <summary>
/// Semantic search input. DocumentId is optional; when omitted, all indexed documents are searched.
/// </summary>
public sealed class DocumentSearchRequest
{
    public string Query { get; set; } = string.Empty;
    public Guid? DocumentId { get; set; }
    public int Limit { get; set; } = 5;
}
