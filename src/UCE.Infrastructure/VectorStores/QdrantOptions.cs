namespace UCE.Infrastructure.VectorStores;

public sealed class QdrantOptions
{
    public const string SectionName = "Qdrant";
    public string BaseUrl { get; set; } = "http://127.0.0.1:6333/";
    public string CollectionName { get; set; } = "uce_chunks";
    public int VectorSize { get; set; } = 1024;
    public string Distance { get; set; } = "Cosine";
    public int UpsertBatchSize { get; set; } = 64;
    public int TimeoutSeconds { get; set; } = 300;
    public string? ApiKey { get; set; }
}
