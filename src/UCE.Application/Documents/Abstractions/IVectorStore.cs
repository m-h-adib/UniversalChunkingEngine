using UCE.Application.Documents.Models;

namespace UCE.Application.Documents.Abstractions;

public interface IVectorStore
{
    Task UpsertAsync(
        IReadOnlyList<VectorPoint> points,
        CancellationToken cancellationToken = default);
}
