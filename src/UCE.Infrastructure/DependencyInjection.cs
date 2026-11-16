using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UCE.Application.Documents.Abstractions;
using UCE.Application.Documents.Models;
using UCE.Application.Documents.Services;
using UCE.Infrastructure.Chunking;
using UCE.Infrastructure.Documents.Parsing;
using UCE.Infrastructure.Documents.Structure;
using UCE.Infrastructure.Documents.Text;
namespace UCE.Infrastructure;
public static class DependencyInjection
{
 public static IServiceCollection AddInfrastructure(this IServiceCollection services,IConfiguration configuration){services.Configure<StructureBuilderOptions>(configuration.GetSection("StructureBuilder"));services.Configure<HeadingDetectionOptions>(configuration.GetSection("HeadingDetection"));services.Configure<ContentBlockClassifierOptions>(configuration.GetSection("ContentBlockClassifier"));services.AddSingleton<ITextNormalizer,PersianTextNormalizer>();services.AddSingleton<ITextLanguageDetector,TextLanguageDetector>();services.AddSingleton<IHeadingDetector,HeadingDetector>();services.AddSingleton<IContentBlockClassifier,ContentBlockClassifier>();services.AddScoped<IDocumentStructureBuilder,DocumentStructureBuilder>();services.AddSingleton<ITokenCounter,ApproximateTokenCounter>();services.AddScoped<IChunkingService,StructuralChunkingService>();services.AddScoped<IDocumentParser,TxtDocumentParser>();services.AddScoped<IDocumentParser,DocxDocumentParser>();services.AddScoped<IDocumentParser,PdfDocumentParser>();services.AddScoped<IDocumentParserResolver,DocumentParserResolver>();services.AddScoped<DocumentImportService>();return services;}
}