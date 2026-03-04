using System.Text;
using System.Text.Json;

namespace UniHelper;

public class QdrantClient
{
    private readonly HttpClient HttpClient;
    private readonly string Collection;

    public QdrantClient(string baseUrl, string apiKey, string collection, HttpClient? httpClient = null)
    {
        HttpClient = httpClient ?? new HttpClient();
        HttpClient.BaseAddress = new Uri(baseUrl.TrimEnd('/'));
        HttpClient.DefaultRequestHeaders.Add("api-key", apiKey);
        Collection = collection;
    }

    public async Task CreateCollectionAsync(int vectorSize, CancellationToken cancellationToken = default)
    {
        using var get = await HttpClient.GetAsync($"/collections/{Collection}", cancellationToken);
        if (get.IsSuccessStatusCode)
            return;

        var body = new
        {
            vectors = new
            {
                size = vectorSize,
                distance = "Cosine"
            }
        };
        
        var json = JsonSerializer.Serialize(body);
        using var put = await HttpClient.PutAsync($"/collections/{Collection}",
            new StringContent(json, Encoding.UTF8, "application/json"), cancellationToken);
        
        var response = await put.Content.ReadAsStringAsync(cancellationToken);
        put.EnsureSuccessStatusCode();
    }

    public async Task UpsertAsync(IEnumerable<object> points, CancellationToken cancellationToken = default)
    {
        var body = new { points = points };
        var json = JsonSerializer.Serialize(body);
        
        using var response = await HttpClient.PutAsync($"/collections/{Collection}/points?wait=true", 
            new StringContent(json, Encoding.UTF8, "application/json"), cancellationToken);
        
        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<JsonElement> SearchAsync(float[] vector, int limit = 8,
        CancellationToken cancellationToken = default)
    {
        var body = new
        {
            vector = vector,
            limit = limit,
            with_payload = true,
        };
        
        var json = JsonSerializer.Serialize(body);
        using var response = await HttpClient.PostAsync($"/collections/{Collection}/points/search", 
            new StringContent(json, Encoding.UTF8, "application/json"), cancellationToken);
        
        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        response.EnsureSuccessStatusCode();
        
        using var document = JsonDocument.Parse(responseJson);
        return document.RootElement.GetProperty("result").Clone();
    }
}