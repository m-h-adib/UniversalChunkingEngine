namespace UCE.Application.Documents.Models;

/// <summary>
/// A configurable regular-expression rule for recognizing document headings.
/// Patterns are evaluated against the trimmed paragraph text.
/// </summary>
public sealed class HeadingPatternRule
{
    public string Pattern { get; set; } = string.Empty;
    public int Level { get; set; } = 2;
    public double Confidence { get; set; } = 0.85;
}
