using Microsoft.Extensions.Options;
using UCE.Application.Documents.Models;
using UCE.Infrastructure.Documents.Structure;
using Xunit;
namespace UCE.UnitTests;
public class HeadingDetectorTests { private readonly HeadingDetector _sut=new(Options.Create(new HeadingDetectionOptions())); [Theory][InlineData("فصل اول")][InlineData("باب دوم")][InlineData("Chapter 1")] public void DetectsHeadings(string text)=>Assert.True(_sut.Detect(new ParsedParagraph{Text=text}).IsHeading); [Fact] public void DoesNotDetectOrdinarySentence()=>Assert.False(_sut.Detect(new ParsedParagraph{Text="این یک جمله عادی است."}).IsHeading); }