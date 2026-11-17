using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using UCE.Application.Documents.Abstractions;

namespace UCE.Infrastructure.Embeddings;

public sealed class BgeM3EmbeddingGenerator : IEmbeddingGenerator
{
    private const int ExpectedDimensions = 1024;
    private readonly HttpClient _httpClient;
    private readonly BgeM3Options _options;

    public BgeM3EmbeddingGenerator(
        HttpClient httpClient,
        IOptions<BgeM3Options> options)
    {
        _httpClient = httpClient;
        _options = options.Value;

        if (_options.BatchSize < 1)
        {
            throw new InvalidOperationException("BgeM3:BatchSize must be greater than zero.");
        }
    }

    public async Task<IReadOnlyList<float[]>> GenerateAsync(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(texts);

        if (texts.Count == 0)
        {
            return [];
        }

        if (texts.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("Embedding input texts cannot be null or whitespace.", nameof(texts));
        }

        var result = new List<float[]>(texts.Count);

        for (var offset = 0; offset < texts.Count; offset += _options.BatchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var batch = texts
                .Skip(offset)
                .Take(_options.BatchSize)
                .ToArray();

            using var response = await _httpClient.PostAsJsonAsync(
                "embed",
                new BgeM3Request
                {
                    Texts = batch,
                    BatchSize = _options.BatchSize,
                    Normalize = _options.Normalize
                },
                cancellationToken);

            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException(
                    $"BGE-M3 returned {(int)response.StatusCode} ({response.StatusCode}): {responseContent}",
                    null,
                    response.StatusCode);
            }

            var payload = JsonSerializer.Deserialize<BgeM3Response>(
                responseContent,
                JsonSerializerOptions.Web);

            if (payload?.Embeddings is null)
            {
                throw new InvalidOperationException("BGE-M3 returned an empty or invalid response.");
            }

            if (payload.Count != batch.Length || payload.Embeddings.Count != batch.Length)
            {
                throw new InvalidOperationException(
                    $"BGE-M3 batch count mismatch. Expected {batch.Length}, received {payload.Embeddings.Count}.");
            }

            if (payload.Dimensions != ExpectedDimensions)
            {
                throw new InvalidOperationException(
                    $"Unexpected BGE-M3 dimensions. Expected {ExpectedDimensions}, received {payload.Dimensions}.");
            }

            foreach (var vector in payload.Embeddings)
            {
                if (vector is null || vector.Length != payload.Dimensions)
                {
                    throw new InvalidOperationException("BGE-M3 returned a vector with an invalid dimension.");
                }

                result.Add(vector);
            }
        }

        return result;
    }

    private sealed class BgeM3Request
    {
        [JsonPropertyName("texts")]
        public IReadOnlyList<string> Texts { get; init; } = [];

        [JsonPropertyName("batch_size")]
        public int BatchSize { get; init; }

        [JsonPropertyName("normalize")]
        public bool Normalize { get; init; }
    }

    private sealed class BgeM3Response
    {
        [JsonPropertyName("dimensions")]
        public int Dimensions { get; init; }

        [JsonPropertyName("count")]
        public int Count { get; init; }

        [JsonPropertyName("embeddings")]
        public List<float[]> Embeddings { get; init; } = [];
    }
}
