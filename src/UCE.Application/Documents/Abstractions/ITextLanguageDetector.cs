using UCE.Domain.Enums;
namespace UCE.Application.Documents.Abstractions;
public interface ITextLanguageDetector { DocumentLanguage Detect(string text); }