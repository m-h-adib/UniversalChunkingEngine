using Microsoft.Extensions.Options;
using UCE.Application.Documents.Models;
using UCE.Domain.Enums;
using UCE.Infrastructure.Documents.Structure;
using Xunit;
namespace UCE.UnitTests;
public class ContentBlockClassifierTests { private readonly ContentBlockClassifier _sut=new(Options.Create(new ContentBlockClassifierOptions())); [Theory][InlineData("۱. مقدمه")][InlineData("2. Introduction")][InlineData("۳- موضوع")] public void DetectsOrderedList(string text)=>Assert.Equal(ContentBlockType.OrderedListItem,_sut.Classify(new ParsedParagraph{Text=text}).Type); [Fact] public void DetectsBullet()=>Assert.Equal(ContentBlockType.UnorderedListItem,_sut.Classify(new ParsedParagraph{Text="• مورد اول"}).Type); [Fact] public void DetectsVerse()=>Assert.Equal(ContentBlockType.QuranVerse,_sut.Classify(new ParsedParagraph{Text="﴿بسم الله الرحمن الرحیم﴾"}).Type); [Fact] public void DetectsFootnoteStyle()=>Assert.Equal(ContentBlockType.Footnote,_sut.Classify(new ParsedParagraph{Text="یادداشت",StyleName="FootnoteText"}).Type); }