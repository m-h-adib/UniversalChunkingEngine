using UCE.Application.Documents.Models;
namespace UCE.Application.Documents.Abstractions;
public interface IDocumentStructureBuilder { DocumentStructure Build(ParsedDocument document); }