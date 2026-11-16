using UCE.Application.Documents.Abstractions;
namespace UCE.Infrastructure.Documents.Parsing;
public sealed class DocumentParserResolver(IEnumerable<IDocumentParser> parsers):IDocumentParserResolver
{ public IDocumentParser Resolve(string fileName)=>parsers.FirstOrDefault(p=>p.CanParse(fileName))??throw new NotSupportedException($"File extension is not supported: {Path.GetExtension(fileName)}"); }