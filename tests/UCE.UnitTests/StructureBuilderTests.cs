using Microsoft.Extensions.Options;
using UCE.Application.Documents.Models;
using UCE.Domain.Enums;
using UCE.Infrastructure.Documents.Structure;
using Xunit;
namespace UCE.UnitTests;
public class StructureBuilderTests { [Fact] public void KeepsHeadingAndParagraphInFlatList(){var hd=new HeadingDetector(Options.Create(new HeadingDetectionOptions()));var cl=new ContentBlockClassifier(Options.Create(new ContentBlockClassifierOptions()));var sut=new DocumentStructureBuilder(Options.Create(new StructureBuilderOptions()),hd,cl);var doc=new ParsedDocument{FullText="فصل اول\n\nاین متن است.",ParsedParagraphs=[new(){Order=0,Text="فصل اول",StartPosition=0,EndPosition=8},new(){Order=1,Text="این متن است.",StartPosition=10,EndPosition=22}]};var result=sut.Build(doc);Assert.Equal(2,result.Paragraphs.Count);Assert.Equal(ContentBlockType.Heading,result.Paragraphs[0].Type);Assert.Equal(ContentBlockType.Paragraph,result.Paragraphs[1].Type);} }