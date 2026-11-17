using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using UCE.Application.Documents.Abstractions;
using UCE.Application.Documents.Models;

namespace UCE.Infrastructure.VectorStores;

/// <summary>
/// Qdrant REST implementation. Creates the configured collection on first use.
/// </summary>
public sealed class QdrantVectorStore : IVectorStore
{
    private readonly HttpClient _httpClient;
    private readonly QdrantOptions _options;

    public QdrantVectorStore(
        HttpClient httpClient,
        IOptions<QdrantOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;

        if (_options.VectorSize < 1)
            throw new InvalidOperationException("Qdrant:VectorSize must be greater than zero.");

        if (_options.UpsertBatchSize < 1)
            throw new InvalidOperationException("Qdrant:UpsertBatchSize must be greater than zero.");

        if (string.IsNullOrWhiteSpace(_options.CollectionName))
            throw new InvalidOperationException("Qdrant:CollectionName is required.");

        if (_options.CollectionName.Any(c => !(char.IsLetterOrDigit(c) || c is '_' or '-')))
            throw new InvalidOperationException("Qdrant:CollectionName may contain only letters, digits, '_' and '-'.");
    }

    public async Task UpsertAsync(
        IReadOnlyList<VectorPoint> points,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(points);

        if (points.Count == 0)
            return;

        foreach (var point in points)
        {
            if (point.Vector is null || point.Vector.Length != _options.VectorSize)
            {
                throw new ArgumentException(
                    $"Every vector must contain exactly {_options.VectorSize} values.",
                    nameof(points));
            }
        }

        await EnsureCollectionAsync(cancellationToken);

        for (var offset = 0; offset < points.Count; offset += _options.UpsertBatchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var batch = points
                .Skip(offset)
                .Take(_options.UpsertBatchSize)
                .Select(point => new
                {
                    id = point.Id,
                    vector = point.Vector,
                    payload = point.Payload
                })
                .ToArray();

            using var request = CreateRequest(
                HttpMethod.Put,
                $"collections/{_options.CollectionName}/points?wait=true",
                new { points = batch });

            using var response = await _httpClient.SendAsync(
                request,
                cancellationToken);

            await EnsureSuccessAsync(response, cancellationToken);
        }
    }


    public async Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
        float[] vector,
        Guid? documentId,
        int limit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(vector);

        if (vector.Length != _options.VectorSize)
            throw new ArgumentException($"Query vector must contain exactly {_options.VectorSize} values.", nameof(vector));

        if (limit is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(limit), "Limit must be between 1 and 100.");

        object? filter = documentId.HasValue
            ? new
            {
                must = new[]
                {
                    new
                    {
                        key = "documentId",
                        match = new { value = documentId.Value }
                    }
                }
            }
            : null;

        using var request = CreateRequest(
            HttpMethod.Post,
            $"collections/{_options.CollectionName}/points/query",
            new
            {
                query = vector,
                limit,
                with_payload = true,
                filter
            });

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        if (!json.RootElement.TryGetProperty("result", out var resultElement) ||
            !resultElement.TryGetProperty("points", out var pointsElement) ||
            pointsElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("Qdrant search response does not contain result.points.");
        }

        var results = new List<VectorSearchResult>();
        foreach (var point in pointsElement.EnumerateArray())
        {
            if (!point.TryGetProperty("payload", out var payload) ||
                payload.ValueKind != JsonValueKind.Object)
                continue;

            var mapped = MapSearchResult(point, payload);
            if (mapped is not null)
                results.Add(mapped);
        }

        return results;
    }

    private static VectorSearchResult? MapSearchResult(
        JsonElement point,
        JsonElement payload)
    {
        var idText = ReadString(point, "id");
        if (!Guid.TryParse(idText, out var id))
            return null;

        var documentIdText = ReadString(payload, "documentId");
        if (!Guid.TryParse(documentIdText, out var documentId))
            return null;

        return new VectorSearchResult
        {
            Id = id,
            Score = ReadDouble(point, "score"),
            DocumentId = documentId,
            DocumentTitle = ReadString(payload, "documentTitle"),
            SourceFileName = ReadString(payload, "sourceFileName"),
            Language = ReadString(payload, "language"),
            ChunkOrder = ReadInt(payload, "chunkOrder"),
            Content = ReadString(payload, "content") ?? string.Empty,
            TokenCount = ReadInt(payload, "tokenCount"),
            OverlapTokenCount = ReadInt(payload, "overlapTokenCount"),
            SectionId = ReadNullableGuid(payload, "sectionId"),
            SectionTitle = ReadString(payload, "sectionTitle"),
            SectionLevel = ReadNullableInt(payload, "sectionLevel"),
            SectionDepth = ReadInt(payload, "sectionDepth"),
            ParentSectionId = ReadNullableGuid(payload, "parentSectionId"),
            ParentSectionTitle = ReadString(payload, "parentSectionTitle"),
            SectionPath = ReadStringArray(payload, "sectionPath"),
            StartPosition = ReadInt(payload, "startPosition"),
            EndPosition = ReadInt(payload, "endPosition"),
            StartPage = ReadNullableInt(payload, "startPage"),
            EndPage = ReadNullableInt(payload, "endPage"),
            DominantBlockType = ReadString(payload, "dominantBlockType")
        };
    }

    private static string? ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int ReadInt(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.TryGetInt32(out var number)
            ? number
            : 0;

    private static int? ReadNullableInt(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number &&
        value.TryGetInt32(out var number) ? number : null;

    private static double ReadDouble(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.TryGetDouble(out var number)
            ? number
            : 0d;

    private static Guid? ReadNullableGuid(JsonElement element, string name) =>
        Guid.TryParse(ReadString(element, name), out var value) ? value : null;

    private static IReadOnlyList<string> ReadStringArray(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
            return [];

        return value.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString()!)
            .ToArray();
    }

    private async Task EnsureCollectionAsync(CancellationToken cancellationToken)
    {
        using var getRequest = CreateRequest(
            HttpMethod.Get,
            $"collections/{_options.CollectionName}");

        using var getResponse = await _httpClient.SendAsync(
            getRequest,
            cancellationToken);

        if (getResponse.IsSuccessStatusCode)
            return;

        if (getResponse.StatusCode != HttpStatusCode.NotFound)
            await EnsureSuccessAsync(getResponse, cancellationToken);

        using var createRequest = CreateRequest(
            HttpMethod.Put,
            $"collections/{_options.CollectionName}",
            new
            {
                vectors = new
                {
                    size = _options.VectorSize,
                    distance = _options.Distance
                }
            });

        using var createResponse = await _httpClient.SendAsync(
            createRequest,
            cancellationToken);

        if (createResponse.IsSuccessStatusCode)
            return;

        // Another API instance may have created the collection concurrently.
        if (createResponse.StatusCode == HttpStatusCode.Conflict)
        {
            using var retryRequest = CreateRequest(
                HttpMethod.Get,
                $"collections/{_options.CollectionName}");

            using var retryResponse = await _httpClient.SendAsync(
                retryRequest,
                cancellationToken);

            if (retryResponse.IsSuccessStatusCode)
                return;
        }

        await EnsureSuccessAsync(createResponse, cancellationToken);
    }

    private HttpRequestMessage CreateRequest(
        HttpMethod method,
        string path,
        object? body = null)
    {
        var request = new HttpRequestMessage(method, path);

        if (body is not null)
            request.Content = JsonContent.Create(body);

        if (!string.IsNullOrWhiteSpace(_options.ApiKey))
            request.Headers.TryAddWithoutValidation("api-key", _options.ApiKey);

        return request;
    }

    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
            return;

        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        throw new HttpRequestException(
            $"Qdrant returned {(int)response.StatusCode} ({response.StatusCode}): {body}",
            null,
            response.StatusCode);
    }
}
