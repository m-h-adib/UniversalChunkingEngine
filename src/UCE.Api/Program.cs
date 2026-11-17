using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.OpenApi.Models;
using UCE.Application.Documents.Models;
using UCE.Application.Documents.Services;
using UCE.Application.Documents.Abstractions;
using UCE.Infrastructure;
using UCE.Domain.Enums;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();
builder.Services.Configure<FormOptions>(options => options.MultipartBodyLengthLimit = 50 * 1024 * 1024);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options => options.SwaggerDoc("v1", new OpenApiInfo
{
    Title = "Universal Chunking Engine API",
    Version = "v1",
    Description = "Universal Document Analysis and Chunking Engine"
}));
builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();
app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "Universal Chunking Engine API v1");
    options.RoutePrefix = "swagger";
});
app.UseHttpsRedirection();
app.MapControllers();

app.MapGet("/health", () => Results.Ok(new
{
    status = "ok",
    service = "UniversalChunkingEngine"
})).WithName("HealthCheck");

app.MapPost("/api/documents/analyze",
    async Task<IResult> (IFormFile file, DocumentImportService importer, CancellationToken ct) =>
    {
        if (file is null || file.Length == 0)
            return Results.BadRequest(new { error = "فایل خالی است." });
        if (file.Length > 50 * 1024 * 1024)
            return Results.BadRequest(new { error = "حداکثر حجم فایل ۵۰ مگابایت است." });

        await using var stream = file.OpenReadStream();
        try
        {
            var (doc, structure) = await importer.ImportAsync(stream, file.FileName, ct);
            return Results.Ok(new
            {
                doc.Title, doc.Author, doc.SourceFileName, doc.Language,
                CharacterCount = doc.FullText.Length,
                ParagraphCount = doc.ParsedParagraphs.Count,
                TableCount = doc.Tables.Count,
                SectionCount = structure.Sections.Count,
                Paragraphs = structure.Paragraphs,
                Sections = structure.Sections
            });
        }
        catch (NotSupportedException ex) { return Results.BadRequest(new { error = ex.Message }); }
        catch (InvalidDataException ex) { return Results.BadRequest(new { error = ex.Message }); }
    })
    .DisableAntiforgery().WithName("AnalyzeDocument").WithTags("Documents");

app.MapPost("/api/documents/chunk",
    async Task<IResult> (
        IFormFile file, DocumentImportService importer, IChunkingService chunker,
        ISemanticChunkingService semanticChunker,
        CancellationToken ct, [FromForm] int? targetTokens, [FromForm] int? maxTokens,
        [FromForm] int? overlapTokens, [FromForm] ChunkingStrategy? strategy) =>
    {
        if (file is null || file.Length == 0)
            return Results.BadRequest(new { error = "فایل خالی است." });
        if (file.Length > 50 * 1024 * 1024)
            return Results.BadRequest(new { error = "حداکثر حجم فایل ۵۰ مگابایت است." });

        var options = new ChunkingOptions
        {
            Strategy = strategy ?? ChunkingStrategy.Adaptive,
            TargetChunkTokens = targetTokens ?? 450,
            MaxChunkTokens = maxTokens ?? 700,
            OverlapTokens = overlapTokens ?? 60
        };

        await using var stream = file.OpenReadStream();
        try
        {
            var (doc, structure) = await importer.ImportAsync(stream, file.FileName, ct);
            var chunks = options.Strategy is ChunkingStrategy.Semantic or ChunkingStrategy.Hybrid
                ? await semanticChunker.CreateChunksAsync(structure, options, ct)
                : chunker.CreateChunks(structure, options);
            return Results.Ok(new { doc.Title, doc.Language, ChunkCount = chunks.Count, Chunks = chunks });
        }
        catch (NotSupportedException ex) { return Results.BadRequest(new { error = ex.Message }); }
        catch (InvalidDataException ex) { return Results.BadRequest(new { error = ex.Message }); }
    })
    .DisableAntiforgery().WithName("ChunkDocument").WithTags("Documents");

app.MapPost("/api/documents/chunk-embed",
    async Task<IResult> (
        IFormFile file, DocumentImportService importer, ChunkEmbeddingService embeddingService,
        CancellationToken ct, [FromForm] int? targetTokens, [FromForm] int? maxTokens,
        [FromForm] int? overlapTokens, [FromForm] ChunkingStrategy? strategy) =>
    {
        if (file is null || file.Length == 0)
            return Results.BadRequest(new { error = "فایل خالی است." });
        if (file.Length > 50 * 1024 * 1024)
            return Results.BadRequest(new { error = "حداکثر حجم فایل ۵۰ مگابایت است." });

        var options = new ChunkingOptions
        {
            Strategy = strategy ?? ChunkingStrategy.Adaptive,
            TargetChunkTokens = targetTokens ?? 450,
            MaxChunkTokens = maxTokens ?? 700,
            OverlapTokens = overlapTokens ?? 60
        };

        await using var stream = file.OpenReadStream();
        try
        {
            var (doc, structure) = await importer.ImportAsync(stream, file.FileName, ct);
            var embeddedChunks = await embeddingService.CreateEmbeddedChunksAsync(structure, options, ct);
            return Results.Ok(new
            {
                doc.Title, doc.Language,
                ChunkCount = embeddedChunks.Count,
                EmbeddingModel = "BAAI/bge-m3",
                EmbeddingDimensions = 1024,
                Chunks = embeddedChunks
            });
        }
        catch (NotSupportedException ex) { return Results.BadRequest(new { error = ex.Message }); }
        catch (InvalidDataException ex) { return Results.BadRequest(new { error = ex.Message }); }
        catch (HttpRequestException ex)
        {
            return Results.Problem(title: "سرویس BGE-M3 در دسترس نیست.", detail: ex.Message,
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (InvalidOperationException ex)
        {
            return Results.Problem(title: "پاسخ سرویس Embedding معتبر نیست.", detail: ex.Message,
                statusCode: StatusCodes.Status502BadGateway);
        }
    })
    .DisableAntiforgery().WithName("ChunkAndEmbedDocument").WithTags("Documents");

app.MapPost("/api/documents/index",
    async Task<IResult> (
        IFormFile file, DocumentImportService importer, DocumentIndexingService indexingService,
        CancellationToken ct, [FromForm] int? targetTokens, [FromForm] int? maxTokens,
        [FromForm] int? overlapTokens, [FromForm] ChunkingStrategy? strategy) =>
    {
        if (file is null || file.Length == 0)
            return Results.BadRequest(new { error = "فایل خالی است." });
        if (file.Length > 50 * 1024 * 1024)
            return Results.BadRequest(new { error = "حداکثر حجم فایل ۵۰ مگابایت است." });

        var options = new ChunkingOptions
        {
            Strategy = strategy ?? ChunkingStrategy.Adaptive,
            TargetChunkTokens = targetTokens ?? 450,
            MaxChunkTokens = maxTokens ?? 700,
            OverlapTokens = overlapTokens ?? 60
        };

        await using var stream = file.OpenReadStream();
        try
        {
            var (document, structure) = await importer.ImportAsync(stream, file.FileName, ct);
            var result = await indexingService.IndexAsync(document, structure, options, ct);
            return Results.Ok(new
            {
                result.DocumentId,
                document.Title,
                document.SourceFileName,
                document.Language,
                result.ChunkCount,
                result.VectorDimensions,
                VectorModel = "BAAI/bge-m3",
                Storage = "Qdrant",
                Status = "indexed"
            });
        }
        catch (NotSupportedException ex) { return Results.BadRequest(new { error = ex.Message }); }
        catch (InvalidDataException ex) { return Results.BadRequest(new { error = ex.Message }); }
        catch (HttpRequestException ex)
        {
            return Results.Problem(title: "ارتباط با سرویس Embedding یا Qdrant برقرار نشد.",
                detail: ex.Message, statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (InvalidOperationException ex)
        {
            return Results.Problem(title: "پاسخ یکی از سرویس‌های زیرساخت معتبر نیست.",
                detail: ex.Message, statusCode: StatusCodes.Status502BadGateway);
        }
    })
    .DisableAntiforgery().WithName("IndexDocument").WithTags("Documents");


app.MapPost("/api/documents/search",
    async Task<IResult> (
        DocumentSearchRequest request,
        DocumentSearchService searchService,
        CancellationToken ct) =>
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Query))
            return Results.BadRequest(new { error = "متن پرس‌وجو الزامی است." });

        if (request.Limit is < 1 or > 100)
            return Results.BadRequest(new { error = "Limit باید بین ۱ تا ۱۰۰ باشد." });

        try
        {
            var results = await searchService.SearchAsync(request, ct);
            return Results.Ok(new
            {
                request.Query,
                request.DocumentId,
                request.Limit,
                ResultCount = results.Count,
                EmbeddingModel = "BAAI/bge-m3",
                Results = results
            });
        }
        catch (HttpRequestException ex)
        {
            return Results.Problem(
                title: "ارتباط با سرویس BGE-M3 یا Qdrant برقرار نشد.",
                detail: ex.Message,
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (InvalidOperationException ex)
        {
            return Results.Problem(
                title: "پاسخ یکی از سرویس‌های زیرساخت معتبر نیست.",
                detail: ex.Message,
                statusCode: StatusCodes.Status502BadGateway);
        }
    })
    .WithName("SearchDocuments").WithTags("Documents");

app.Run();

public partial class Program
{
}
