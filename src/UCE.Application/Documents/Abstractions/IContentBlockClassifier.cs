using UCE.Application.Documents.Models;
namespace UCE.Application.Documents.Abstractions;
public interface IContentBlockClassifier { ContentBlockClassification Classify(ParsedParagraph paragraph); }