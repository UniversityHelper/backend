namespace UniversityHelper.Shared;

public class AiSettings
{
    public string? ChatEndpoint { get; set; }
    public string? ChatModel { get; set; }
    public string? EmbeddingEndpoint { get; set; }
    public string? QdrantEndpoint { get; set; }
    public string? ApiKey { get; set; }
    public string? OnnxModelPath { get; set; }
}
