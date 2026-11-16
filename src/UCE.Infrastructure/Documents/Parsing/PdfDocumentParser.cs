using System.Text;
using UglyToad.PdfPig;
using UCE.Application.Documents.Abstractions;
using UCE.Application.Documents.Models;
using UCE.Domain.Enums;
namespace UCE.Infrastructure.Documents.Parsing;
public sealed class PdfDocumentParser(ITextNormalizer normalizer,ITextLanguageDetector languageDetector):IDocumentParser
{
 public bool CanParse(string fileName)=>Path.GetExtension(fileName).Equals(".pdf",StringComparison.OrdinalIgnoreCase);
 public Task<ParsedDocument> ParseAsync(Stream stream,string fileName,CancellationToken cancellationToken=default){cancellationToken.ThrowIfCancellationRequested();using var pdf=PdfDocument.Open(stream);var doc=new ParsedDocument{SourceFileName=Path.GetFileName(fileName),Title=Path.GetFileNameWithoutExtension(fileName)};var full=new StringBuilder();int order=0;foreach(var page in pdf.GetPages()){cancellationToken.ThrowIfCancellationRequested();var pageText=normalizer.Normalize(page.Text??"");int start=full.Length;if(full.Length>0)full.AppendLine().AppendLine();start=full.Length;full.Append(pageText);doc.Pages.Add(new(){PageNumber=page.Number,Text=pageText,StartPosition=start,EndPosition=full.Length});foreach(var para in System.Text.RegularExpressions.Regex.Split(pageText,@"\n\s*\n")){var t=para.Trim();if(t.Length==0)continue;int local=pageText.IndexOf(t,StringComparison.Ordinal);doc.ParsedParagraphs.Add(new(){Text=t,Order=order++,StartPosition=start+Math.Max(0,local),EndPosition=start+Math.Max(0,local)+t.Length,PageNumber=page.Number});}}doc.FullText=full.ToString();doc.Language=languageDetector.Detect(doc.FullText);for(int i=0;i<doc.ParsedParagraphs.Count;i++)doc.Blocks.Add(new(){Order=i,Kind=ParsedBlockKind.Paragraph,ParagraphIndex=i});return Task.FromResult(doc);}
}