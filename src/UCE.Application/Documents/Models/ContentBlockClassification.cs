using UCE.Domain.Enums;
namespace UCE.Application.Documents.Models;
public sealed class ContentBlockClassification { public ContentBlockType Type { get; set; } = ContentBlockType.Unknown; public double Confidence { get; set; } public string? Marker { get; set; } public List<string> Reasons { get; set; } = []; }