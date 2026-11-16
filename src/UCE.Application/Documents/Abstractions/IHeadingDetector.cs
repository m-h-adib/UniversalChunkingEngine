using UCE.Application.Documents.Models;
namespace UCE.Application.Documents.Abstractions;
public interface IHeadingDetector { HeadingDetectionResult Detect(ParsedParagraph paragraph); }