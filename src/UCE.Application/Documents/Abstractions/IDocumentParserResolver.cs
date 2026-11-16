namespace UCE.Application.Documents.Abstractions;
public interface IDocumentParserResolver { IDocumentParser Resolve(string fileName); }