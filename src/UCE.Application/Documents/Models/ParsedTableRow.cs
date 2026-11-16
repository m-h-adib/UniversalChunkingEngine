namespace UCE.Application.Documents.Models;
public sealed class ParsedTableRow { public int Index { get; set; } public List<ParsedTableCell> Cells { get; set; } = []; }