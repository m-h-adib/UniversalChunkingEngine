namespace UCE.Infrastructure.Embeddings;

public sealed class BgeM3Options
{
    public const string SectionName = "BgeM3";

    public string BaseUrl { get; set; } = "http://127.0.0.1:8000/";
    public int BatchSize { get; set; } = 8;
    public bool Normalize { get; set; } = true;
    public int TimeoutSeconds { get; set; } = 300;
}
