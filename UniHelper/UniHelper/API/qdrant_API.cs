using System.Text;
using System.Text.Json;

namespace UniHelper;

public class QdrantClient
{
    private readonly HttpClient HttpClient;
    private readonly string Collection;
    private readonly string BaseUrl;
    private readonly string ApiKey;
    

    public QdrantClient(string baseUrl, string apiKey, string collection, HttpClient? httpClient = null)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException("QDrant ApiKey is empty. Check appsettings.json: Qdrant:ApiKey");

        HttpClient = httpClient ?? new HttpClient();
        BaseUrl = baseUrl.TrimEnd('/');
        ApiKey = apiKey;
        Collection = collection;
    }

    public async Task CreateCollectionAsync(int vectorSize, CancellationToken cancellationToken = default)
    {
        using var getRequest = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/collections/{Collection}");
        getRequest.Headers.Add("ApiKey", ApiKey);
        using var get = await HttpClient.SendAsync(getRequest, cancellationToken);
        
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
        using var putRequest = new HttpRequestMessage(HttpMethod.Put, $"{BaseUrl}/collections/{Collection}");
        putRequest.Headers.Add("api-key", ApiKey);
        putRequest.Content = new StringContent(json, Encoding.UTF8, "application/json");

        using var put = await HttpClient.SendAsync(putRequest, cancellationToken);
        await put.Content.ReadAsStringAsync(cancellationToken);
        put.EnsureSuccessStatusCode();
    }

    public async Task UpsertAsync(IEnumerable<object> points, CancellationToken cancellationToken = default)
    {
        var body = new { points };
        var json = JsonSerializer.Serialize(body);
        
        using var request = new HttpRequestMessage(HttpMethod.Put, $"{BaseUrl}/collections/{Collection}/points?wait=true");
        request.Headers.Add("api-key", ApiKey);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        
        using var response = await HttpClient.SendAsync(request, cancellationToken);
        await response.Content.ReadAsStringAsync(cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<JsonElement> SearchAsync(float[] vector, int limit = 8,
        CancellationToken cancellationToken = default)
    {
        var body = new
        {
            vector,
            limit,
            with_payload = true,
        };
        
        var json = JsonSerializer.Serialize(body);
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/collections/{Collection}/points/search");
        request.Headers.Add("api-key", ApiKey);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        
        using var response = await HttpClient.SendAsync(request, cancellationToken);
        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        response.EnsureSuccessStatusCode();
        
        using var document = JsonDocument.Parse(responseJson);
        return document.RootElement.GetProperty("result").Clone();
    }
}