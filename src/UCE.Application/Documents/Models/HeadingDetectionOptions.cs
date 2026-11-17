namespace UCE.Application.Documents.Models;

public sealed class HeadingDetectionOptions
{
    public double MinimumConfidence { get; set; } = 0.60;
    public int MaximumHeadingLength { get; set; } = 180;
    public bool UseStyleInformation { get; set; } = true;
    public bool UseNumberingPatterns { get; set; } = true;
    public bool UsePersianPatterns { get; set; } = true;
    public bool UseArabicPatterns { get; set; } = true;
    public bool UseEnglishPatterns { get; set; } = true;
    public bool UseTextPatterns { get; set; } = true;

    /// <summary>
    /// Additional document-specific heading rules. This makes the detector
    /// extensible to books and languages not covered by the built-in patterns.
    /// </summary>
    public List<HeadingPatternRule> CustomPatterns { get; set; } = [];
}
