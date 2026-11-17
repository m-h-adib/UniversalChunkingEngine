using System.Net.Http.Json;
using System.Text.Json;
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
        You are a semantic document segmentation engine.
        Find only meaningful topic changes between consecutive paragraphs.
        Keep explanations, examples, evidence, conditions and exceptions with
        the topic they explain. A heading starts a new topic when appropriate.
        Return a JSON object with exactly one property named splitAfter.
        splitAfter is an array of zero-based paragraph indexes. An index N means
        a new topic starts at paragraph N+1. Do not include the final paragraph
        index. Return [] when there is no topic change.
        Do not summarize, rewrite, translate or reproduce the paragraph text.
        """;

    private static readonly object ResponseFormat = new
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

        var payload = new
        {
            paragraphs = paragraphs.Select((p, index) => new
            {
                index,
                isHeading = p.IsHeading,
                section = p.SectionTitle,
                text = p.Text
            })
        };

        var userContent = """
            Decide where the subject changes in this ordered list of paragraphs.
            Consider the meaning of each paragraph in relation to its neighbors.
            Output only the required JSON object.
            """;

        var firstResponse = await RequestAsync(
            userContent + Environment.NewLine + JsonSerializer.Serialize(payload),
            cancellationToken);

        if (TryParseBoundaries(firstResponse, paragraphs.Count, out var boundaries, out var error))
            return boundaries;

        // Small local models occasionally ignore the requested schema. Retry once
        // with a compact correction request; never silently switch chunking modes.
        var retryContent = $$"""
            Your previous answer was invalid: {{error}}
            Correct the answer using the paragraph list below.
            Return ONLY this JSON shape: {"splitAfter":[]}
            splitAfter must contain zero-based integer indexes from 0 to {{paragraphs.Count - 2}}.
            Do not include the final paragraph index or any other property.
            Paragraphs:
            {{JsonSerializer.Serialize(payload)}}
            """;

        var retryResponse = await RequestAsync(retryContent, cancellationToken);
        if (TryParseBoundaries(retryResponse, paragraphs.Count, out boundaries, out error))
            return boundaries;

        throw new InvalidOperationException(
            $"Ollama returned an invalid semantic-boundary response after one retry: {error}");
    }

    private async Task<string> RequestAsync(string userContent, CancellationToken cancellationToken)
    {
        var request = new
        {
            model = _options.Model,
            stream = false,
            format = ResponseFormat,
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

        // Be tolerant of markdown fences or a short preamble, while still
        // requiring a valid JSON object with the exact contract.
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

    private sealed class OllamaChatResponse
    {
        public OllamaMessage? Message { get; set; }
    }

    private sealed class OllamaMessage
    {
        public string? Content { get; set; }
    }
}
