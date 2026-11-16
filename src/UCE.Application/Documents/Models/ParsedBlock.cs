namespace UCE.Application.Documents.Models;
public enum ParsedBlockKind { Paragraph, Table }
public sealed class ParsedBlock
{
    public int Order { get; set; }
    public ParsedBlockKind Kind { get; set; }
    public int ParagraphIndex { get; set; } = -1;
    public int TableIndex { get; set; } = -1;
    public int StartPosition { get; set; }
    public int EndPosition { get; set; }
    public int? PageNumber { get; set; }
}