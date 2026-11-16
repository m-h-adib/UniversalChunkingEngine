using UCE.Application.Documents.Abstractions;
using UCE.Application.Documents.Models;
using UCE.Domain.Enums;
namespace UCE.Infrastructure.Chunking;
public sealed class StructuralChunkingService(ITokenCounter counter) : IChunkingService
{
 public IReadOnlyList<ChunkDto> CreateChunks(DocumentStructure structure,ChunkingOptions options)
 {
  ArgumentNullException.ThrowIfNull(structure);
  if(options.MaxChunkTokens<=0||options.TargetChunkTokens<=0||options.OverlapTokens<0)
   throw new ArgumentOutOfRangeException(nameof(options));
  var result=new List<ChunkDto>();
  var current=new List<DocumentParagraph>();
  var tokens=0;
  var overlapText=string.Empty;
  void Flush()
  {
   if(current.Count==0)return;
   var first=current[0];
   var last=current[^1];
   var body=string.Join("\n\n",current.Select(p=>p.Text));
   var content=string.IsNullOrWhiteSpace(overlapText)?body:overlapText+"\n\n"+body;
   var pageNumbers=current.Where(x=>x.PageNumber.HasValue).Select(x=>x.PageNumber!.Value).ToList();
   result.Add(new ChunkDto
   {
    Order=result.Count,Content=content,TokenCount=counter.Count(content),
    SectionId=first.SectionId,SectionTitle=FindSectionTitle(structure.Sections,first.SectionId),
    StartPosition=first.StartPosition,EndPosition=last.EndPosition,
    StartPage=pageNumbers.Count>0?pageNumbers.Min():null,
    EndPage=pageNumbers.Count>0?pageNumbers.Max():null,
    DominantBlockType=current.GroupBy(x=>x.Type).OrderByDescending(g=>g.Count()).First().Key
   });
   overlapText=options.OverlapTokens>0?TailWords(body,options.OverlapTokens):string.Empty;
   current.Clear();tokens=0;
  }
  foreach(var p in structure.Paragraphs.OrderBy(x=>x.Order))
  {
   var pt=counter.Count(p.Text);
   var headingBoundary=options.PreserveHeadings&&p.IsHeading&&current.Count>0;
   var overLimit=current.Count>0&&tokens+pt>options.MaxChunkTokens;
   var targetReached=current.Count>0&&tokens>=options.TargetChunkTokens;
   if(headingBoundary||overLimit||targetReached)
   {
    Flush();
    if(headingBoundary)overlapText=string.Empty;
   }
   current.Add(p);tokens+=pt;
   if(tokens>=options.MaxChunkTokens)Flush();
  }
  Flush();
  return result;
 }
 private static string TailWords(string text,int count)
 {
  if(count<=0||string.IsNullOrWhiteSpace(text))return string.Empty;
  var words=text.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries);
  return string.Join(" ",words.Skip(Math.Max(0,words.Length-count)));
 }

 private static string? FindSectionTitle(IEnumerable<DocumentStructureSection> sections,Guid? id){foreach(var s in sections){if(s.Id==id)return s.Title;var child=FindSectionTitle(s.Children,id);if(child!=null)return child;}return null;}
}