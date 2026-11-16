using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using UCE.Application.Documents.Abstractions;
using UCE.Application.Documents.Models;
using UCE.Domain.Enums;
namespace UCE.Infrastructure.Documents.Structure;
public sealed class ContentBlockClassifier(IOptions<ContentBlockClassifierOptions> options) : IContentBlockClassifier
{
 private readonly ContentBlockClassifierOptions _options=options.Value;
 private static readonly Regex Ordered=new(@"^(?<m>(?:[0-9۰-۹٠-٩]+[.)\-ـ]|[الف-ی][.)\-ـ]))\s+",RegexOptions.Compiled);
 private static readonly Regex Unordered=new(@"^(?<m>[\u2022\u25CF\u25AA\u25E6\-\*])\s+",RegexOptions.Compiled);
 private static readonly Regex Footnote=new(@"^(?<m>\[\d+\]|\([0-9۰-۹]+\)|[⁰¹²³⁴⁵⁶⁷⁸⁹]+)\s*",RegexOptions.Compiled);
 public ContentBlockClassification Classify(ParsedParagraph p) { var t=p.Text?.Trim()??""; if(t.Length<_options.MinimumTextLength)return Make(ContentBlockType.Unknown,0,null,"متن کوتاه است"); var style=p.StyleName??""; if(_options.DetectFootnotes && (style.Contains("Footnote",StringComparison.OrdinalIgnoreCase)||style.Contains("پاورقی")))return Make(ContentBlockType.Footnote,.95,null,"Footnote style"); if(_options.DetectQuranVerses && Regex.IsMatch(t,"﴿.+?﴾"))return Make(ContentBlockType.QuranVerse,.95,null,"Quranic brackets"); if(_options.DetectFootnotes){var f=Footnote.Match(t);if(f.Success)return Make(ContentBlockType.Footnote,.72,f.Groups["m"].Value,"Footnote marker");} if(_options.DetectOrderedLists){var m=Ordered.Match(t);if(m.Success)return Make(ContentBlockType.OrderedListItem,.88,m.Groups["m"].Value,"Ordered list marker");} if(_options.DetectUnorderedLists){var m=Unordered.Match(t);if(m.Success)return Make(ContentBlockType.UnorderedListItem,.94,m.Groups["m"].Value,"Bullet marker");} if(_options.DetectQuotes && ((t.StartsWith('«')&&t.EndsWith('»'))||(t.StartsWith('“')&&t.EndsWith('”'))||(t.StartsWith('"')&&t.EndsWith('"'))))return Make(ContentBlockType.Quote,.85,null,"Quotation marks"); return Make(ContentBlockType.Paragraph,.8,null,"Default paragraph"); }
 private static ContentBlockClassification Make(ContentBlockType type,double confidence,string? marker,string reason)=>new(){Type=type,Confidence=confidence,Marker=marker,Reasons=[reason]};
}