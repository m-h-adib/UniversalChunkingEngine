using System.Text.RegularExpressions;
using UCE.Application.Documents.Abstractions;
namespace UCE.Infrastructure.Documents.Text;
public sealed class PersianTextNormalizer : ITextNormalizer
{
 public string Normalize(string text)
 { if (string.IsNullOrEmpty(text)) return string.Empty; text = text.Replace('ي','ی').Replace('ى','ی').Replace('ك','ک').Replace('ـ',' '); text = text.Replace("\r\n","\n").Replace('\r','\n'); text = Regex.Replace(text, @"[\t\f\v ]+", " "); text = Regex.Replace(text, @" *\n *", "\n"); text = Regex.Replace(text, @"\n{3,}", "\n\n"); return text.Trim(); }
}