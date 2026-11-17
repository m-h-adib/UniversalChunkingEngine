namespace UCE.Application.Documents.Models;

public sealed class VectorPoint
{
    public Guid Id { get; set; }
    public float[] Vector { get; set; } = [];
    public object Payload { get; set; } = new();
}
