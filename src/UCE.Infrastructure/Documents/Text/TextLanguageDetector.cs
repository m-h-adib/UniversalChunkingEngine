using UCE.Application.Documents.Abstractions;
using UCE.Domain.Enums;
namespace UCE.Infrastructure.Documents.Text;
public sealed class TextLanguageDetector : ITextLanguageDetector
{
 public DocumentLanguage Detect(string text) { if (string.IsNullOrWhiteSpace(text)) return DocumentLanguage.Unknown; int fa=0, ar=0, en=0; foreach(var c in text) { if ((c>='پ'&&c<='ی') || c=='ک' || c=='گ' || c=='ژ' || c=='چ') fa++; else if (c>='ء'&&c<='ي') ar++; else if ((c>='A'&&c<='Z')||(c>='a'&&c<='z')) en++; } int total=fa+ar+en; if(total==0)return DocumentLanguage.Unknown; if((fa>0?1:0)+(ar>0?1:0)+(en>0?1:0)>1)return DocumentLanguage.Mixed; if(fa>=ar && fa>=en)return DocumentLanguage.Persian; if(ar>=en)return DocumentLanguage.Arabic; return DocumentLanguage.English; }
}