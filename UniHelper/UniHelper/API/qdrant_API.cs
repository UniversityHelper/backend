using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

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
        getRequest.Headers.Add("api-key", ApiKey);
        using var get = await HttpClient.SendAsync(getRequest, cancellationToken);

        if (!get.IsSuccessStatusCode)
        {
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
            put.EnsureSuccessStatusCode();
        }
        
        await CreatePayloadIndexAsync("university", "keyword", cancellationToken);
        await CreatePayloadIndexAsync("level", "keyword", cancellationToken);
    }

    private async Task CreatePayloadIndexAsync(string fieldName, string fieldSchema,
        CancellationToken cancellationToken)
    {
        var body = new
        {
            field_name = fieldName, 
            field_schema = fieldSchema
        };
        var json = JsonSerializer.Serialize(body);
        using var request = new HttpRequestMessage(HttpMethod.Put, $"{BaseUrl}/collections/{Collection}/index");
        
        request.Headers.Add("api-key", ApiKey);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        
        await HttpClient.SendAsync(request, cancellationToken);
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

    public async Task<JsonElement> SearchAsync(float[] vector, int limit = 8, string? university = null, 
        string? level = null, CancellationToken cancellationToken = default)
    {
        var conditions = new List<object>();
        if (!string.IsNullOrEmpty(university))
            conditions.Add(new { key = "university", match = new { value = university } });
        
        if (!string.IsNullOrEmpty(level))
            conditions.Add(new { key = "level", match = new { value = level } });
        
        var filter = conditions.Count > 0 ? new { must = conditions } : null;
        var body = new
        {
            vector,
            limit,
            filter,
            with_payload = true,
        };
        var options = new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        var json = JsonSerializer.Serialize(body, options);
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/collections/{Collection}/points/search");
        request.Headers.Add("api-key", ApiKey);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        
        using var response = await HttpClient.SendAsync(request, cancellationToken);
        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Qdrant Error {response.StatusCode}: {responseJson}");
        
        using var document = JsonDocument.Parse(responseJson);
        return document.RootElement.GetProperty("result").Clone();
    }
}