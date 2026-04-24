using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace UniHelper;

public class OpenAiChatClient
{
    private readonly HttpClient HttpClient;
    private readonly string Model;
    private readonly string ApiKey;

    public OpenAiChatClient(string apiKey, string model = "gpt-4o-mini", HttpClient? httpClient = null)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException("OpenAI ApiKey for LLM is empty. Check appsettings.json: OpenAI:ApiKey");

        HttpClient = httpClient ?? new HttpClient();
        ApiKey = apiKey;
        Model = model;
    }

    public async Task<string> ChatAsync(string system, string user, CancellationToken cancellationToken = default)
    {
        var body = new
        {
            model = Model,
            messages = new object[]
            {
                new { role = "system", content = system },
                new { role = "user", content = user }
            },
            temperature = 0
        };
        
        var json = JsonSerializer.Serialize(body);
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ApiKey);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        
        using var response = await HttpClient.SendAsync(request, cancellationToken);
        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        
        response.EnsureSuccessStatusCode();
        
        using var document = JsonDocument.Parse(responseJson);
        return document.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";
    }
}