using UCE.Application.Documents.Models;
namespace UCE.Application.Documents.Abstractions;
public interface IDocumentParser { bool CanParse(string fileName); Task<ParsedDocument> ParseAsync(Stream stream, string fileName, CancellationToken cancellationToken = default); }