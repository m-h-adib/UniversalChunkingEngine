using System.Text;
using UCE.Application.Documents.Abstractions;
using UCE.Application.Documents.Models;
using UCE.Domain.Enums;
namespace UCE.Infrastructure.Documents.Parsing;
public sealed class TxtDocumentParser(ITextNormalizer normalizer,ITextLanguageDetector languageDetector):IDocumentParser
{
 public bool CanParse(string fileName)=>Path.GetExtension(fileName).Equals(".txt",StringComparison.OrdinalIgnoreCase);
 public async Task<ParsedDocument> ParseAsync(Stream stream,string fileName,CancellationToken cancellationToken=default){using var reader=new StreamReader(stream,Encoding.UTF8,true,1024,true);var raw=await reader.ReadToEndAsync(cancellationToken);var text=normalizer.Normalize(raw);var doc=new ParsedDocument{SourceFileName=Path.GetFileName(fileName),Title=Path.GetFileNameWithoutExtension(fileName),FullText=text,Language=languageDetector.Detect(text)};int order=0,search=0;foreach(var part in System.Text.RegularExpressions.Regex.Split(text,@"\n\s*\n")){var p=part.Trim();if(p.Length==0)continue;var idx=text.IndexOf(p,search,StringComparison.Ordinal);if(idx<0)idx=search;doc.ParsedParagraphs.Add(new(){Text=p,Order=order++,StartPosition=idx,EndPosition=idx+p.Length});search=idx+p.Length;}for(int i=0;i<doc.ParsedParagraphs.Count;i++)doc.Blocks.Add(new(){Order=i,Kind=ParsedBlockKind.Paragraph,ParagraphIndex=i});return doc;}
}