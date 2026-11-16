using UCE.Application.Documents.Abstractions;
using UCE.Application.Documents.Models;
namespace UCE.Application.Documents.Services;
public sealed class DocumentImportService(IDocumentParserResolver resolver, IDocumentStructureBuilder structureBuilder)
{
 public async Task<(ParsedDocument Document, DocumentStructure Structure)> ImportAsync(Stream stream, string fileName, CancellationToken ct = default)
 { var parser = resolver.Resolve(fileName); var document = await parser.ParseAsync(stream, fileName, ct); return (document, structureBuilder.Build(document)); }
}