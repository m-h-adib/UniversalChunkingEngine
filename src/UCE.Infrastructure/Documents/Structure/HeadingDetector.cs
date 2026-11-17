using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using UCE.Application.Documents.Abstractions;
using UCE.Application.Documents.Models;

namespace UCE.Infrastructure.Documents.Structure;

public sealed class HeadingDetector : IHeadingDetector
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);

    private static readonly Regex PersianLevel1 =
        new(@"^(فصل|باب|کتاب|بخش)\s+", RegexOptions.Compiled | RegexOptions.CultureInvariant, RegexTimeout);
    private static readonly Regex PersianLevel2 =
        new(@"^(مبحث|گفتار|قسمت|عنوان)\s+", RegexOptions.Compiled | RegexOptions.CultureInvariant, RegexTimeout);
    private static readonly Regex PersianLevel3 =
        new(@"^(مسئله|مسأله|مسائل|ماده|تبصره|پرسش|سؤال|سوال|موضوع)\s*(?:شماره\s*)?(?:[0-9۰-۹٠-٩]+|اول|دوم|سوم|چهارم|پنجم)?\s*[:：.)\-ـ]\s*\S+",
            RegexOptions.Compiled | RegexOptions.CultureInvariant, RegexTimeout);

    private static readonly Regex ArabicLevel1 =
        new(@"^(الباب|الفصل|الكتاب|القسم)\s+", RegexOptions.Compiled | RegexOptions.CultureInvariant, RegexTimeout);
    private static readonly Regex ArabicLevel2 =
        new(@"^(المبحث|المطلب|القسم الفرعي)\s+", RegexOptions.Compiled | RegexOptions.CultureInvariant, RegexTimeout);
    private static readonly Regex ArabicLevel3 =
        new(@"^(المسألة|المادة|التنبيه|السؤال|الحكم|الفرع)\s*(?:[0-9٠-٩]+)?\s*[:：.)\-ـ]\s*\S+",
            RegexOptions.Compiled | RegexOptions.CultureInvariant, RegexTimeout);

    private static readonly Regex EnglishLevel1 =
        new(@"^(chapter|part|book)\s+", RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant, RegexTimeout);
    private static readonly Regex EnglishLevel2 =
        new(@"^(section|subsection|topic)\s+", RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant, RegexTimeout);
    private static readonly Regex EnglishLevel3 =
        new(@"^(article|clause|rule|question|problem|example)\s*(?:no\.?\s*)?(?:[0-9]+)?\s*[:：.)\-]\s*\S+",
            RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant, RegexTimeout);

    // A single number followed by a dot is ambiguous with an ordered-list item.
    // Only hierarchical numbers such as 1.2 or 2.3.1 are detected by this rule.
    private static readonly Regex Numbered = new(
        @"^(?<n>[0-9۰-۹٠-٩]+(?:[.٫][0-9۰-۹٠-٩]+)+)(?:[.)\-ـ]|\s+)\s*\S+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant, RegexTimeout);

    private readonly HeadingDetectionOptions _options;
    private readonly (Regex Pattern, int Level, double Confidence)[] _customPatterns;

    public HeadingDetector(IOptions<HeadingDetectionOptions> options)
    {
        _options = options.Value;

        _customPatterns = _options.CustomPatterns
            .Where(x => !string.IsNullOrWhiteSpace(x.Pattern))
            .Select(x =>
            {
                if (x.Level < 1 || x.Level > 6)
                    throw new ArgumentOutOfRangeException(nameof(x.Level), "Custom heading level must be between 1 and 6.");
                if (x.Confidence is < 0 or > 1)
                    throw new ArgumentOutOfRangeException(nameof(x.Confidence), "Custom heading confidence must be between 0 and 1.");

                return (
                    new Regex(x.Pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant, RegexTimeout),
                    x.Level,
                    x.Confidence);
            })
            .ToArray();
    }

    public HeadingDetectionResult Detect(ParsedParagraph paragraph)
    {
        ArgumentNullException.ThrowIfNull(paragraph);

        var text = paragraph.Text?.Trim() ?? string.Empty;
        if (text.Length == 0)
            return new HeadingDetectionResult();

        double score = 0;
        int? level = null;
        var reasons = new List<string>();
        var style = paragraph.StyleName ?? string.Empty;

        void ApplyMatch(bool matched, int candidateLevel, double confidence, string reason)
        {
            if (!matched || confidence < score)
                return;

            level = candidateLevel;
            score = confidence;
            reasons.Add(reason);
        }

        if (_options.UseStyleInformation &&
            Regex.IsMatch(style, @"^Heading[1-6]$", RegexOptions.IgnoreCase, RegexTimeout))
        {
            level = int.Parse(Regex.Match(style, @"[1-6]", RegexOptions.None, RegexTimeout).Value);
            score = 0.95;
            reasons.Add("DOCX heading style");
        }

        if (_options.UseTextPatterns)
        {
            if (_options.UsePersianPatterns)
            {
                ApplyMatch(PersianLevel1.IsMatch(text), 1, 0.90, "Persian major heading keyword");
                ApplyMatch(PersianLevel2.IsMatch(text), 2, 0.85, "Persian section heading keyword");
                ApplyMatch(PersianLevel3.IsMatch(text), 3, 0.88, "Persian topic heading keyword");
            }

            if (_options.UseArabicPatterns)
            {
                ApplyMatch(ArabicLevel1.IsMatch(text), 1, 0.90, "Arabic major heading keyword");
                ApplyMatch(ArabicLevel2.IsMatch(text), 2, 0.85, "Arabic section heading keyword");
                ApplyMatch(ArabicLevel3.IsMatch(text), 3, 0.88, "Arabic topic heading keyword");
            }

            if (_options.UseEnglishPatterns)
            {
                ApplyMatch(EnglishLevel1.IsMatch(text), 1, 0.90, "English major heading keyword");
                ApplyMatch(EnglishLevel2.IsMatch(text), 2, 0.85, "English section heading keyword");
                ApplyMatch(EnglishLevel3.IsMatch(text), 3, 0.88, "English topic heading keyword");
            }

            foreach (var rule in _customPatterns)
                ApplyMatch(rule.Pattern.IsMatch(text), rule.Level, rule.Confidence, "Custom heading pattern");

            if (_options.UseNumberingPatterns && Numbered.IsMatch(NormalizeDigits(text)))
            {
                var number = Numbered.Match(NormalizeDigits(text)).Groups["n"].Value;
                var numberedLevel = Math.Clamp(number.Count(c => c is '.' or '٫') + 1, 2, 6);
                ApplyMatch(true, numberedLevel, 0.78, "Hierarchical numbered heading");
            }
        }

        if (text.Length > _options.MaximumHeadingLength)
            score -= 0.25;
        if (Regex.IsMatch(text, @"[.!?؟،؛:]$", RegexOptions.None, RegexTimeout))
            score -= 0.15;

        score = Math.Clamp(score, 0, 1);

        return new HeadingDetectionResult
        {
            IsHeading = level.HasValue && score >= _options.MinimumConfidence,
            Level = level,
            Confidence = score,
            Reasons = reasons
        };
    }

    private static string NormalizeDigits(string value) =>
        value.Replace('۰', '0').Replace('۱', '1').Replace('۲', '2').Replace('۳', '3')
            .Replace('۴', '4').Replace('۵', '5').Replace('۶', '6').Replace('۷', '7')
            .Replace('۸', '8').Replace('۹', '9').Replace('٠', '0').Replace('١', '1')
            .Replace('٢', '2').Replace('٣', '3').Replace('٤', '4').Replace('٥', '5')
            .Replace('٦', '6').Replace('٧', '7').Replace('٨', '8').Replace('٩', '9');
}
