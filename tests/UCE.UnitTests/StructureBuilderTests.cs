using Microsoft.Extensions.Options;
using UCE.Application.Documents.Models;
using UCE.Domain.Enums;
using UCE.Infrastructure.Documents.Structure;
using Xunit;

namespace UCE.UnitTests;

public class StructureBuilderTests
{
    private static DocumentStructureBuilder CreateSut()
    {
        var detector = new HeadingDetector(Options.Create(new HeadingDetectionOptions()));
        var classifier = new ContentBlockClassifier(Options.Create(new ContentBlockClassifierOptions()));
        return new DocumentStructureBuilder(
            Options.Create(new StructureBuilderOptions()), detector, classifier);
    }

    [Fact]
    public void KeepsHeadingAndParagraphInFlatList()
    {
        var document = new ParsedDocument
        {
            FullText = "فصل اول\n\nاین متن است.",
            ParsedParagraphs =
            [
                new() { Order = 0, Text = "فصل اول", StartPosition = 0, EndPosition = 8 },
                new() { Order = 1, Text = "این متن است.", StartPosition = 10, EndPosition = 22 }
            ]
        };

        var result = CreateSut().Build(document);

        Assert.Equal(2, result.Paragraphs.Count);
        Assert.Equal(ContentBlockType.Heading, result.Paragraphs[0].Type);
        Assert.Equal(ContentBlockType.Paragraph, result.Paragraphs[1].Type);
    }

    [Fact]
    public void PreservesTablePositionAndOrderBetweenParagraphs()
    {
        var document = new ParsedDocument
        {
            FullText = "متن اول\n\nردیف 1: ستون 1: الف\n\nمتن دوم",
            ParsedParagraphs =
            [
                new() { Order = 0, Text = "متن اول", StartPosition = 0, EndPosition = 7 },
                new() { Order = 2, Text = "متن دوم", StartPosition = 29, EndPosition = 36 }
            ],
            Tables =
            [
                new()
                {
                    Order = 1, RowCount = 1, ColumnCount = 1,
                    Rows =
                    [
                        new()
                        {
                            Index = 0,
                            Cells = [new() { RowIndex = 0, ColumnIndex = 0, Text = "الف" }]
                        }
                    ]
                }
            ],
            Blocks =
            [
                new() { Order = 0, Kind = ParsedBlockKind.Paragraph, ParagraphIndex = 0, StartPosition = 0, EndPosition = 7 },
                new() { Order = 1, Kind = ParsedBlockKind.Table, TableIndex = 0, StartPosition = 9, EndPosition = 27 },
                new() { Order = 2, Kind = ParsedBlockKind.Paragraph, ParagraphIndex = 1, StartPosition = 29, EndPosition = 36 }
            ]
        };

        var result = CreateSut().Build(document);

        Assert.Equal(3, result.Paragraphs.Count);
        Assert.Equal(ContentBlockType.Table, result.Paragraphs[1].Type);
        Assert.Contains("الف", result.Paragraphs[1].Text);
        Assert.Equal("متن دوم", result.Paragraphs[2].Text);
    }
}