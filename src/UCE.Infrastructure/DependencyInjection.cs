using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using UCE.Application.Documents.Abstractions;
using UCE.Application.Documents.Models;
using UCE.Application.Documents.Services;
using UCE.Infrastructure.Chunking;
using UCE.Infrastructure.Documents.Parsing;
using UCE.Infrastructure.Documents.Structure;
using UCE.Infrastructure.Documents.Text;
using UCE.Infrastructure.Embeddings;
using UCE.Infrastructure.VectorStores;

namespace UCE.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<StructureBuilderOptions>(configuration.GetSection("StructureBuilder"));
        services.Configure<HeadingDetectionOptions>(configuration.GetSection("HeadingDetection"));
        services.Configure<ContentBlockClassifierOptions>(configuration.GetSection("ContentBlockClassifier"));
        services.Configure<BgeM3Options>(configuration.GetSection(BgeM3Options.SectionName));
        services.Configure<QdrantOptions>(configuration.GetSection(QdrantOptions.SectionName));
        services.Configure<SemanticChunkingOptions>(configuration.GetSection(SemanticChunkingOptions.SectionName));

        services.AddSingleton<ITextNormalizer, PersianTextNormalizer>();
        services.AddSingleton<ITextLanguageDetector, TextLanguageDetector>();
        services.AddSingleton<IHeadingDetector, HeadingDetector>();
        services.AddSingleton<IContentBlockClassifier, ContentBlockClassifier>();
        services.AddScoped<IDocumentStructureBuilder, DocumentStructureBuilder>();
        services.AddSingleton<ITokenCounter, ApproximateTokenCounter>();
        services.AddScoped<IChunkingService, StructuralChunkingService>();
        services.AddScoped<ISemanticChunkingService, SemanticChunkingService>();
        services.AddHttpClient<ISemanticBoundaryDetector, OllamaSemanticBoundaryDetector>((sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<SemanticChunkingOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseUrl, UriKind.Absolute);
            client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
        });
        services.AddScoped<ChunkEmbeddingService>();
        services.AddScoped<DocumentIndexingService>();
        services.AddScoped<DocumentSearchService>();

        services.AddScoped<IDocumentParser, TxtDocumentParser>();
        services.AddScoped<IDocumentParser, DocxDocumentParser>();
        services.AddScoped<IDocumentParser, PdfDocumentParser>();
        services.AddScoped<IDocumentParserResolver, DocumentParserResolver>();
        services.AddScoped<DocumentImportService>();

        services.AddHttpClient<IEmbeddingGenerator, BgeM3EmbeddingGenerator>((sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<BgeM3Options>>().Value;
            client.BaseAddress = new Uri(options.BaseUrl, UriKind.Absolute);
            client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
        });

        services.AddHttpClient<IVectorStore, QdrantVectorStore>((sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<QdrantOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseUrl, UriKind.Absolute);
            client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
        });

        return services;
    }
}
