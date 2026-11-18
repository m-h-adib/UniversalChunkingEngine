using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Encodings.Web;
using Microsoft.Extensions.Options;
using UCE.Application.Documents.Abstractions;

namespace UCE.Infrastructure.Chunking;

/// <summary>
/// Uses Ollama to identify topic boundaries. The model returns indexes only;
/// chunks are always assembled from the original source paragraphs.
/// </summary>
public sealed class OllamaSemanticBoundaryDetector(
    HttpClient httpClient,
    IOptions<SemanticChunkingOptions> options) : ISemanticBoundaryDetector
{
    private readonly SemanticChunkingOptions _options = options.Value;

    private const string SystemPrompt = """
        You divide a document into coherent retrieval topics.
        Return only JSON: {"splitAfter":[]}
        splitAfter contains zero-based paragraph indexes. Index N means a new
        topic starts at paragraph N+1. Do not include the final paragraph index.
        Split only when the next paragraph begins a genuinely different topic.
        Keep a complete narrative, argument, explanation, example, evidence,
        correction, and its conclusion together. Do not isolate short follow-up
        paragraphs or one-line qualifications as separate topics.
        A paragraph marked heading=true is a strong topic boundary: normally
        start a new topic before that heading.
        Prefer coherent multi-paragraph units over one-paragraph chunks.
        Never rewrite, omit, reorder, or summarize source paragraph text.
        """;

    private static readonly object ResponseSchema = new
    {
        type = "object",
        properties = new
        {
            splitAfter = new
            {
                type = "array",
                items = new { type = "integer" }
            }
        },
        required = new[] { "splitAfter" },
        additionalProperties = false
    };

    public async Task<IReadOnlySet<int>> DetectBoundariesAsync(
        IReadOnlyList<SemanticParagraphInput> paragraphs,
        CancellationToken cancellationToken = default)
    {
        if (paragraphs.Count < 2)
            return new HashSet<int>();

        var payload = paragraphs.Select((p, index) => new
        {
            index,
            heading = p.IsHeading,
            sectionTitle = p.SectionTitle,
            text = p.Text
        }).ToArray();

        // Keep Persian text readable inside the prompt. The default JSON encoder
        // escapes non-ASCII characters, which makes small local models see long
        // sequences such as \\u0627 instead of natural Persian text.
        var serializedParagraphs = JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });
        var firstPrompt = """
            Identify genuine topic changes in these numbered paragraphs.
            Keep connected narratives and explanations together; avoid tiny
            standalone chunks. A heading usually starts a new topic.
            Return a JSON object with one property: splitAfter.
            """ + Environment.NewLine + serializedParagraphs;

        var firstResponse = await RequestAsync(
            firstPrompt, useJsonSchema: true, cancellationToken);

        if (TryParseBoundaries(firstResponse, paragraphs.Count, out var boundaries, out var error))
            return boundaries;

        // Gemma 3 1B can struggle with constrained JSON schemas. Retry using
        // Ollama's simpler JSON mode and a shorter instruction.
        var retryPrompt = $$"""
            Output only valid JSON in this exact shape:
            {"splitAfter":[]}
            splitAfter is an array of zero-based integers. A value N means
            a new topic begins at paragraph N+1. Valid values: 0 through {{paragraphs.Count - 2}}.
            Keep related explanations, examples, corrections and conclusions together.
            Avoid boundaries that would create a tiny standalone chunk.
            A heading usually starts a new topic. Do not output prose.
            Paragraphs:
            {{serializedParagraphs}}
            """;

        var retryResponse = await RequestAsync(
            retryPrompt, useJsonSchema: false, cancellationToken);

        if (TryParseBoundaries(retryResponse, paragraphs.Count, out boundaries, out error))
            return boundaries;

        throw new InvalidOperationException(
            $"Ollama returned an invalid semantic-boundary response after JSON-mode retry: {error}. " +
            $"Model response: {CreateDiagnosticExcerpt(retryResponse)}");
    }

    private async Task<string> RequestAsync(
        string userContent,
        bool useJsonSchema,
        CancellationToken cancellationToken)
    {
        var request = new
        {
            model = _options.Model,
            stream = false,
            think = false,
            format = useJsonSchema ? ResponseSchema : (object)"json",
            messages = new[]
            {
                new { role = "system", content = SystemPrompt },
                new { role = "user", content = userContent }
            },
            options = new { temperature = 0, seed = 42 }
        };

        using var response = await httpClient.PostAsJsonAsync(
            "api/chat", request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var envelope = await response.Content.ReadFromJsonAsync<OllamaChatResponse>(
            cancellationToken: cancellationToken);

        return envelope?.Message?.Content ?? string.Empty;
    }

    private static bool TryParseBoundaries(
        string content,
        int paragraphCount,
        out IReadOnlySet<int> boundaries,
        out string error)
    {
        boundaries = new HashSet<int>();
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(content))
        {
            error = "response content was empty";
            return false;
        }

        var jsonStart = content.IndexOf('{');
        var jsonEnd = content.LastIndexOf('}');
        if (jsonStart < 0 || jsonEnd <= jsonStart)
        {
            error = "response did not contain a JSON object";
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(content[jsonStart..(jsonEnd + 1)]);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("splitAfter", out var values)
                || values.ValueKind != JsonValueKind.Array)
            {
                error = "JSON object must contain a splitAfter array";
                return false;
            }

            var parsed = new HashSet<int>();
            foreach (var value in values.EnumerateArray())
            {
                if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var index))
                {
                    error = "splitAfter must contain only integer indexes";
                    return false;
                }

                if (index < 0 || index >= paragraphCount - 1)
                {
                    error = $"boundary index {index} is outside the valid range 0..{paragraphCount - 2}";
                    return false;
                }

                parsed.Add(index);
            }

            boundaries = parsed;
            return true;
        }
        catch (JsonException)
        {
            error = "response contained malformed JSON";
            return false;
        }
    }

    private static string CreateDiagnosticExcerpt(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return "<empty>";

        const int maxLength = 400;
        var excerpt = content.Length <= maxLength
            ? content
            : content[..maxLength] + "...";

        return JsonSerializer.Serialize(excerpt);
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
