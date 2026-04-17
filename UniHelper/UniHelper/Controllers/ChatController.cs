using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

namespace UniHelper.Controllers;

public class ChatRequest
{
    public string Message { get; set; } = "";
}

public class ChatResponse
{
    public string Answer { get; set; } = "";
    public bool Found { get; set; }
}

public class ChatSource
{
    public string Title { get; set; } = "";
    public string Url { get; set; } = "";
}

[ApiController]
[Route("api/chat")]
public class ChatController(IConfiguration configuration) : ControllerBase
{
    private readonly IConfiguration configuration = configuration;

    [HttpGet("health")]
    public IActionResult Health()
    {
        return Ok(new { status = "ok" });
    }

    [HttpPost("message")]
    public async Task<IActionResult> Message([FromBody] ChatRequest request)
    {
        if (request == null || string.IsNullOrEmpty(request.Message))
            return BadRequest(new { error = "Message is empty" });
        
        var qdrantUrl = configuration["Qdrant:Url"]!;
        var qdrantKey = configuration["Qdrant:ApiKey"]!;
        var collection = configuration["Qdrant:Collection"]!;
        var openAiKey = configuration["OpenAI:ApiKey"]!;
        var embeddingModel = configuration["OpenAI:EmbeddingModel"]!;
        var chatModel = configuration["OpenAI:ChatModel"]!;
        var proxyUrl = configuration["Proxy:ProxyUrl"]!;
        var login = configuration["Proxy:Login"]!;
        var password = configuration["Proxy:Password"]!;

        var webProxy = new WebProxy(proxyUrl)
        {
            Credentials = new NetworkCredential(login, password)
        };
        
        using var handler = new HttpClientHandler();
        handler.Proxy = webProxy;
        handler.UseProxy = true;

        using var httpClient = new HttpClient(handler);
        var embedder = new OpenAiEmbeddingClient(openAiKey, embeddingModel, httpClient);
        var qdrant = new QdrantClient(qdrantUrl, qdrantKey, collection);
        var llm = new OpenAiChatClient(openAiKey, chatModel, httpClient);
        var queryVector = await embedder.EmbeddingAsync(request.Message);
        var hits = await qdrant.SearchAsync(queryVector, limit: 5);
        var contextParts = new List<string>();
        var sources = new List<object>();
        var i = 1;
        
        foreach (var hit in hits.EnumerateArray())
        {
            var payload = hit.GetProperty("payload");
            var text = payload.TryGetProperty("text", out var textProperty)
                ? textProperty.GetString() ?? ""
                : "";
            var url = payload.TryGetProperty("url", out var urlProperty)
                ? urlProperty.GetString() ?? ""
                : "";
            var title = payload.TryGetProperty("title", out var titleProperty)
                ? titleProperty.GetString() ?? "Источник"
                : "Источник";

            if (!string.IsNullOrWhiteSpace(text))
            {
                contextParts.Add($"[{i}] title={title}\nurl={url}\n{text}");
                sources.Add(new
                {
                    title,
                    url
                });

                i++;
            }
        }

        if (contextParts.Count == 0)
        {
            return Ok(new ChatResponse
            {
                Answer = "Не найдено в официальных источниках УрФУ.",
                Found = false,
            });
        }
        
        const string systemPrompt = "Ты AI-помощник по поступлению в университет. " + 
                                    "Отвечай только по переданному CONTEXT. " + 
                                    "Если в контексте нет ответа, скажи: " + 
                                    "\"Не найдено в официальных источниках УрФУ.\" " + 
                                    "Не придумывай факты, даты и числа. ";
        var userPrompt = $"QUESTION:\n{request.Message}\n\n" + $"CONTEXT:\n{string.Join("\n\n", contextParts)}";
        var answer = await llm.ChatAsync(systemPrompt, userPrompt);
        var resultSources = new List<ChatSource>();

        foreach (var source in sources)
        {
            var json = JsonSerializer.Serialize(source);
            var parsed = JsonSerializer.Deserialize<ChatSource>(json);
            
            if (parsed != null)
                resultSources.Add(parsed);
        }

        return Ok(new ChatResponse
        {
            Answer = answer,
            Found = true,
        });
    }
}