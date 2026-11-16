using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using UCE.Application.Documents.Abstractions;
using UCE.Application.Documents.Models;
namespace UCE.Infrastructure.Documents.Structure;
public sealed class HeadingDetector(IOptions<HeadingDetectionOptions> options) : IHeadingDetector
{
 private readonly HeadingDetectionOptions _options=options.Value;
 private static readonly Regex Persian1=new(@"^(فصل|باب|کتاب|بخش)\s+",RegexOptions.Compiled);
 private static readonly Regex Persian2=new(@"^(مبحث|گفتار|قسمت|عنوان)\s+",RegexOptions.Compiled);
 private static readonly Regex Arabic1=new(@"^(الباب|الفصل|الكتاب)\s+",RegexOptions.Compiled);
 private static readonly Regex Arabic2=new(@"^(المبحث|المطلب|المسألة|الفرع)\s+",RegexOptions.Compiled);
 private static readonly Regex English1=new(@"^(chapter|part|book)\s+",RegexOptions.IgnoreCase|RegexOptions.Compiled);
 private static readonly Regex English2=new(@"^(section|subsection|topic)\s+",RegexOptions.IgnoreCase|RegexOptions.Compiled);
 private static readonly Regex Numbered=new(@"^(?<n>[0-9۰-۹٠-٩]+(?:[.٫][0-9۰-۹٠-٩]+)*)(?:[.)\-ـ]|\s+)\s*\S+",RegexOptions.Compiled);
 public HeadingDetectionResult Detect(ParsedParagraph paragraph)
 { var text=paragraph.Text?.Trim()??""; if(text.Length==0)return new(); double score=0; int? level=null; var reasons=new List<string>(); var style=paragraph.StyleName??""; if(_options.UseStyleInformation && Regex.IsMatch(style,@"^Heading[1-6]$",RegexOptions.IgnoreCase)) { var m=Regex.Match(style,@"[1-6]"); level=int.Parse(m.Value); score=.9; reasons.Add("DOCX heading style"); }
 if(_options.UseTextPatterns) { if(_options.UsePersianPatterns && Persian1.IsMatch(text)){level=1;score=Math.Max(score,.85);reasons.Add("Persian heading keyword");} else if(_options.UsePersianPatterns && Persian2.IsMatch(text)){level=2;score=Math.Max(score,.78);reasons.Add("Persian subsection keyword");} else if(_options.UseArabicPatterns && Arabic1.IsMatch(text)){level=1;score=Math.Max(score,.85);reasons.Add("Arabic heading keyword");} else if(_options.UseArabicPatterns && Arabic2.IsMatch(text)){level=2;score=Math.Max(score,.78);reasons.Add("Arabic subsection keyword");} else if(_options.UseEnglishPatterns && English1.IsMatch(text)){level=1;score=Math.Max(score,.85);reasons.Add("English heading keyword");} else if(_options.UseEnglishPatterns && English2.IsMatch(text)){level=2;score=Math.Max(score,.78);reasons.Add("English subsection keyword");} else if(_options.UseNumberingPatterns && Numbered.IsMatch(NormalizeDigits(text))){var n=Numbered.Match(NormalizeDigits(text)).Groups["n"].Value; level=Math.Clamp(n.Count(c=>c=='.')+1,1,6);score=Math.Max(score,.66);reasons.Add("Numbered heading pattern");} }
 if(text.Length>_options.MaximumHeadingLength)score-=.25; if(Regex.IsMatch(text,@"[.!?؟،؛:]$"))score-=.15; score=Math.Clamp(score,0,1); return new(){IsHeading=level.HasValue&&score>=_options.MinimumConfidence,Level=level,Confidence=score,Reasons=reasons}; }
 private static string NormalizeDigits(string s)=>s.Replace('۰','0').Replace('۱','1').Replace('۲','2').Replace('۳','3').Replace('۴','4').Replace('۵','5').Replace('۶','6').Replace('۷','7').Replace('۸','8').Replace('۹','9').Replace('٠','0').Replace('١','1').Replace('٢','2').Replace('٣','3').Replace('٤','4').Replace('٥','5').Replace('٦','6').Replace('٧','7').Replace('٨','8').Replace('٩','9');
}