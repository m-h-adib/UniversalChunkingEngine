using System.Net;
using System.Net.Http.Json;
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
