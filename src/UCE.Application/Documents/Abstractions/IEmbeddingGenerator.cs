namespace UCE.Application.Documents.Abstractions;

/// <summary>
/// Generates dense vector representations for a collection of texts.
/// </summary>
public interface IEmbeddingGenerator
{
    Task<IReadOnlyList<float[]>> GenerateAsync(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken = default);
}
