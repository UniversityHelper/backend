using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace UniHelper;

public class OpenAiEmbeddingClient
{
    private readonly HttpClient HttpClient;
    private readonly string Model;

    public OpenAiEmbeddingClient(string apiKey, string model = "text-embedding-3-large", HttpClient? httpClient = null)
    {
        HttpClient = httpClient ?? new HttpClient();
        HttpClient.BaseAddress = new Uri("https://api.openai.com");
        HttpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        Model = model;
    }

    public async Task<float[]> EmbeddingAsync(string text, CancellationToken cancellationToken = default)
    {
        var vectors = await EmbeddingBatchAsync(new[] { text }, cancellationToken);
        return vectors[0];
    }

    public async Task<float[][]> EmbeddingBatchAsync(IReadOnlyList<string> texts,
        CancellationToken cancellationToken = default)
    {
        for (var i = 0; i < texts.Count; i++)
            if (string.IsNullOrWhiteSpace(texts[i]))
                throw new ArgumentException("Empty text in embeddings batch");

        var body = new
        {
            model = Model,
            input = texts,
        };
        var json = JsonSerializer.Serialize(body);
        using var response = await HttpClient.PostAsync("/v1/embeddings", new StringContent(json, Encoding.UTF8, "application/json"), cancellationToken);
        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        
        response.EnsureSuccessStatusCode();
        
        using var document = JsonDocument.Parse(responseJson);
        var data = document.RootElement.GetProperty("data");
        var result = new float[data.GetArrayLength()][];

        for (var i = 0; i < data.GetArrayLength(); i++)
        {
            var embedded = data[i].GetProperty("embedding");
            var vector = new float[embedded.GetArrayLength()];
            var j = 0;
            
            foreach (var element in embedded.EnumerateArray())
                vector[j++] = (float)element.GetDouble();
            
            result[i] = vector;
        }
        
        return result;
    }
}