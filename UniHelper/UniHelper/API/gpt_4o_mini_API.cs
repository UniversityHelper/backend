using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace UniHelper;

public class OpenAiChatClient
{
    private readonly HttpClient HttpClient;
    private readonly string Model;

    public OpenAiChatClient(string apiKey, string baseUrl, string model = "gpt-4o-mini", HttpClient? httpClient = null)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException("DeepSeek ApiKey is empty. Check appsettings.json: DeepSeek:ApiKey");

        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new ArgumentException("DeepSeek BaseUrl is empty. Check appsettings.json: DeepSeek:BaseUrl");

        HttpClient = httpClient ?? new HttpClient();
        HttpClient.BaseAddress = new Uri("https://api.openai.com");
        HttpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
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
        using var response = await HttpClient.PostAsync("/v1/chat/completions", new StringContent(json, Encoding.UTF8, "application/json"), cancellationToken);
        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        
        response.EnsureSuccessStatusCode();
        
        using var document = JsonDocument.Parse(responseJson);
        return document.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";
    }
}