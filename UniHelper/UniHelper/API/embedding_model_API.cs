using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace UniHelper;

public class OpenAiEmbeddingClient
{
    private readonly HttpClient HttpClient;
    private readonly string Model;
    private readonly string ApiKey;

    public OpenAiEmbeddingClient(string apiKey, string model = "text-embedding-3-large", HttpClient? httpClient = null)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException("OpenAI ApiKey for embedding model is empty. Check appsettings.json: OpenAI:ApiKey");
        
        HttpClient = httpClient ?? new HttpClient();
        ApiKey = apiKey;
        Model = model;
    }

    public async Task<float[]> EmbeddingAsync(string text, CancellationToken cancellationToken = default)
    {
        var vectors = await EmbeddingBatchAsync([text], cancellationToken);
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
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/embeddings");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ApiKey);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        
        using var response = await HttpClient.SendAsync(request, cancellationToken);
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