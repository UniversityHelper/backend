using Microsoft.AspNetCore.Mvc;

namespace UniHelper.Controllers;

public class ChatRequest
{
    public string Message { get; set; } = "";
    public string SessionId { get; set; } = "";
}

public class ChatResponse
{
    public string Answer { get; set; } = "";
    public bool Found { get; set; }
}

[ApiController]
[Route("api/chat")]
public class ChatController(OpenAiChatClient llm, OpenAiEmbeddingClient embedder, QdrantClient qdrant) : ControllerBase
{
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

        const string rewritePrompt = "Ты - AI, который превращает неточные вопросы пользователей в идеальные поисковые запросы для векторной базы данных университета. " +
                                     "Исправляй опечатки, раскрывай аббревиатуры (УрФУ -> Уральский федеральный университет) и делай запрос формальным. " +
                                     "Если это просто приветствие (привет, как дела) или бессмыслица, верни ровно одно слово: 'CHITCHAT'. " +
                                     "Выводи ТОЛЬКО переписанный запрос без кавычек и лишних слов.";
        var optimizedQuery = await llm.ChatAsync(rewritePrompt, request.Message);

        if (optimizedQuery.Contains("CHITCHAT", StringComparison.OrdinalIgnoreCase))
        {
            const string chitchat = "Ты дружелюбный ассистент УрФУ. Поздоровайся и спроси, чем помочь. Отвечай кратко и вежливо.";
            var chatAnswer = await llm.ChatAsync(chitchat, request.Message);
            
            AnalyticsController.RecordChatInteraction(request.SessionId, true);

            return Ok(new ChatResponse
            {
                Answer = chatAnswer,
                Found = true
            });
        }
        
        var queryVector = await embedder.EmbeddingAsync(optimizedQuery);
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
        
        const string systemPrompt = """
                                    Ты - дружелюбный ИИ-помощник Уральского федерального университета (УрФУ). 
                                    Твоя задача - помогать абитуриентам и студентам.
                                    ПРАВИЛА (В ПОРЯДКЕ ПРИОРИТЕТА):
                                    1. УТОЧНЕНИЯ: если запрос пользователя слишком короткий (1-2 слова, например, 'УрФУ') или слишком общий (например, 'расскажи про урфу'), НЕ пытайся угадать ответ. Вежливо попроси уточнить детали (например: "Что именно вас интересует об УрФУ? Направления подготовки, проходные баллы, общежития?"). НЕ здоровайся.
                                    2. ФАКТОЛОГИЯ (СТРОГО!): если вопрос конкретный, отвечай ТОЛЬКО на основе предоставленного CONTEXT.
                                    3. ОТСУТСТВИЕ ДАННЫХ: если запрос конкретный, но в CONTEXT нет информации, честно скажи: 'К сожалению, я не нашел этой информации в официальных источниках УрФУ.' Не придумывай от себя.
                                    """;
        var userPrompt = $"QUESTION:\n{request.Message}\n\n" + $"CONTEXT:\n{string.Join("\n\n", contextParts)}";
        var answer = await llm.ChatAsync(systemPrompt, userPrompt);
        var isFound = !answer.Contains("К сожалению, я не нашел");
        
        AnalyticsController.RecordChatInteraction(request.SessionId, isFound);

        return Ok(new ChatResponse
        {
            Answer = answer,
            Found = isFound
        });
    }
}