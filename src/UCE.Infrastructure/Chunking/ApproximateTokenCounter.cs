using System.Text.RegularExpressions;
using UCE.Application.Documents.Abstractions;
namespace UCE.Infrastructure.Chunking;
public sealed class ApproximateTokenCounter : ITokenCounter { public int Count(string text) { if(string.IsNullOrWhiteSpace(text))return 0; return Regex.Matches(text,@"\w+|[^\s]",RegexOptions.CultureInvariant).Count; } }