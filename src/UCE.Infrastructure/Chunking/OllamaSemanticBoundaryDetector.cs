using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using UCE.Application.Documents.Abstractions;

namespace UCE.Infrastructure.Chunking;

/// <summary>
/// Uses Ollama's native /api/chat endpoint to identify topic boundaries.
/// The model returns indexes only; source text is never rewritten.
/// </summary>
public sealed class OllamaSemanticBoundaryDetector(
    HttpClient httpClient,
    IOptions<SemanticChunkingOptions> options) : ISemanticBoundaryDetector
{
    private readonly SemanticChunkingOptions _options = options.Value;

    private const string SystemPrompt = """
        You are a semantic document segmentation engine.
        Identify only genuine topic boundaries between consecutive paragraphs.
        A boundary means the next paragraph starts a meaningfully different topic,
        subtopic, rule, event, person, argument, or procedure. Do not split merely
        because wording, examples, or paragraph style changes. Keep explanations,
        conditions, exceptions, evidence, and examples with the topic they explain.
        A heading starts a new topic and should have a boundary before it when possible.
        Return ONLY valid JSON in this exact shape:
        {"splitAfter":[0,2]}
        Indexes are zero-based indexes of paragraphs AFTER which a new chunk should start.
        Never return the final paragraph index. Do not summarize or rewrite any text.
        """;

    public async Task<IReadOnlySet<int>> DetectBoundariesAsync(
        IReadOnlyList<SemanticParagraphInput> paragraphs,
        CancellationToken cancellationToken = default)
    {
        if (paragraphs.Count < 2)
            return new HashSet<int>();

        var payload = new
        {
            paragraphs = paragraphs.Select((p, index) => new
            {
                index,
                p.IsHeading,
                section = p.SectionTitle,
                text = p.Text
            })
        };

        var request = new
        {
            model = _options.Model,
            stream = false,
            format = "json",
            messages = new[]
            {
                new { role = "system", content = SystemPrompt },
                new
                {
                    role = "user",
                    content = JsonSerializer.Serialize(payload)
                }
            },
            options = new { temperature = 0 }
        };

        using var response = await httpClient.PostAsJsonAsync(
            "api/chat", request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var envelope = await response.Content.ReadFromJsonAsync<OllamaChatResponse>(
            cancellationToken: cancellationToken);

        var content = envelope?.Message?.Content;
        if (string.IsNullOrWhiteSpace(content))
            throw new InvalidOperationException("Ollama returned an empty semantic-boundary response.");

        using var document = JsonDocument.Parse(content);
        if (!document.RootElement.TryGetProperty("splitAfter", out var values)
            || values.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("Ollama response must contain a splitAfter array.");

        var result = new HashSet<int>();
        foreach (var value in values.EnumerateArray())
        {
            if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var index))
                throw new InvalidOperationException("Ollama returned a non-integer boundary index.");

            if (index < 0 || index >= paragraphs.Count - 1)
                throw new InvalidOperationException($"Ollama returned an out-of-range boundary index: {index}.");

            result.Add(index);
        }

        return result;
    }

    private sealed class OllamaChatResponse
    {
        public OllamaMessage? Message { get; set; }
    }

    private sealed class OllamaMessage
    {
        public string? Content { get; set; }
    }
}
