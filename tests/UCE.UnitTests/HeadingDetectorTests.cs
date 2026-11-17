using Microsoft.Extensions.Options;
using UCE.Application.Documents.Models;
using UCE.Infrastructure.Documents.Structure;
using Xunit;

namespace UCE.UnitTests;

public class HeadingDetectorTests
{
    private readonly HeadingDetector _sut =
        new(Options.Create(new HeadingDetectionOptions()));

    [Theory]
    [InlineData("فصل اول", 1)]
    [InlineData("باب دوم", 1)]
    [InlineData("گفتار اول: ميقات", 2)]
    [InlineData("مسئله4: قرباني حج کودک", 3)]
    [InlineData("ماده ۱۲: شرایط عمومی", 3)]
    [InlineData("Chapter 1", 1)]
    [InlineData("Article 4: Scope", 3)]
    [InlineData("1.2 مبانی", 2)]
    [InlineData("۲٫۳ احکام", 2)]
    public void DetectsHeadingsAtExpectedLevel(string text, int expectedLevel)
    {
        var result = _sut.Detect(new ParsedParagraph { Text = text });

        Assert.True(result.IsHeading);
        Assert.Equal(expectedLevel, result.Level);
    }

    [Theory]
    [InlineData("۱. مقدمه")]
    [InlineData("2. Introduction")]
    [InlineData("۳- موضوع")]
    [InlineData("مسئله این است که باید به میقات برگردد.")]
    [InlineData("The question is whether the rule applies.")]
    public void DoesNotTreatListItemsOrOrdinarySentencesAsHeadings(string text) =>
        Assert.False(_sut.Detect(new ParsedParagraph { Text = text }).IsHeading);

    [Fact]
    public void SupportsDocumentSpecificCustomHeadingPatterns()
    {
        var detector = new HeadingDetector(Options.Create(new HeadingDetectionOptions
        {
            CustomPatterns =
            [
                new HeadingPatternRule
                {
                    Pattern = @"^Unit\s+\d+\s*:",
                    Level = 2,
                    Confidence = 0.92
                }
            ]
        }));

        var result = detector.Detect(new ParsedParagraph { Text = "Unit 3: Advanced topics" });

        Assert.True(result.IsHeading);
        Assert.Equal(2, result.Level);
        Assert.Contains("Custom heading pattern", result.Reasons);
    }

    [Fact]
    public void DoesNotDetectOrdinarySentence() =>
        Assert.False(_sut.Detect(
            new ParsedParagraph { Text = "این یک جمله عادی است." }).IsHeading);
}
