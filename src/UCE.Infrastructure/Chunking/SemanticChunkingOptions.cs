namespace UCE.Infrastructure.Chunking;

public sealed class SemanticChunkingOptions
{
    public const string SectionName = "SemanticChunking";

    public string BaseUrl { get; set; } = "http://127.0.0.1:11434/";
    public string Model { get; set; } = "gemma3:4b";
    public int MaxParagraphsPerRequest { get; set; } = 16;
    public int MaxInputCharacters { get; set; } = 14000;
    public int TimeoutSeconds { get; set; } = 600;
}
