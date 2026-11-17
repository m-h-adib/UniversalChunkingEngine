namespace UCE.Application.Documents.Abstractions;

public sealed record SemanticParagraphInput(
    int Order,
    string Text,
    bool IsHeading,
    string? SectionTitle);

public interface ISemanticBoundaryDetector
{
    /// <summary>
    /// Returns zero-based paragraph indexes after which a semantic boundary exists.
    /// The last paragraph in a batch cannot be returned because it has no right context.
    /// </summary>
    Task<IReadOnlySet<int>> DetectBoundariesAsync(
        IReadOnlyList<SemanticParagraphInput> paragraphs,
        CancellationToken cancellationToken = default);
}
