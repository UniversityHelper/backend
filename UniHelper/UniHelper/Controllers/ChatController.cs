using Microsoft.AspNetCore.Mvc;

namespace UniHelper.Controllers;

public class ChatRequest
{
    public string Message { get; set; } = "";
    public string SessionId { get; set; } = "";
    public List<MessageHistory> History { get; set; } = [];
}

public class ChatResponse
{
    public string Answer { get; set; } = "";
    public bool Found { get; set; }
}

public class MessageHistory
{
    public string Role { get; set; } = "";
    public string Content { get; set; } = "";
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
        
        var historyText = request.History is { Count: > 0 } 
            ? "КОНТЕКСТ ПРЕДЫДУЩЕГО ДИАЛОГА:\n" + string.Join('\n', request.History.Select(history => $"{history.Role}: {history.Content}")) + "\n\n" 
            : "";
        var rewritePrompt = "Ты - AI, который превращает неточные вопросы пользователей в идеальные поисковые запросы для векторной базы данных. " +
                            "ОБЯЗАТЕЛЬНО используй КОНТЕКСТ ПРЕДЫДУЩЕГО ДИАЛОГА, чтобы понимать суть и сохранять название обсуждаемого вуза в новых запросах.\n" +
                            "ПРАВИЛА:\n" +
                            "1. Если это приветствие, бессмыслица ИЛИ вопрос о твоих возможностях, верни ровно одно слово: 'CHITCHAT'.\n" +
                            "2. ВАЖНО: если пользователь спрашивает про СТОРОННИЙ ВУЗ (университет/институт), которого нет в списке [УрФУ, МГУ, НИУ ВШЭ, ИТМО, СПбГУ] — верни ровно одно слово: 'OUT_OF_SCOPE'.\n" +
                            "   ВНИМАНИЕ: не путай сторонние вузы с названиями факультетов, образовательными программами или их аббревиатурами (например, ФКН, Совбак, ВМК, ИРИТ-РТФ)! Если аббревиатура относится к факультету или программе разрешенного вуза — это НЕ сторонний вуз.\n" +
                            "3. В остальных случаях перепиши запрос для поиска: исправь опечатки, РАСКРОЙ аббревиатуры (например, ФКН -> факультет компьютерных наук) и ОБЯЗАТЕЛЬНО добавь название обсуждаемого вуза из контекста, если пользователь не указал его явно. Выведи ТОЛЬКО переписанный запрос без кавычек.\n\n" +
                            historyText;
        var optimizedQuery = await llm.ChatAsync(rewritePrompt, request.Message);

        if (optimizedQuery.Contains("CHITCHAT", StringComparison.OrdinalIgnoreCase))
        {
            const string chitchat = "Ты дружелюбный ассистент по вопросам поступления. " +
                                    "Если тебя спрашивают о том, что ты умеешь или какие вузы знаешь, отвечай СТРОГО по шаблону.\n" +
                                    "ПРАВИЛА:\n" +
                                    "1. Твоя компетенция ограничена ТОЛЬКО пятью вузами: УрФУ, МГУ, НИУ ВШЭ, ИТМО и СПбГУ.\n" +
                                    "2. ЗАПРЕЩЕНО писать 'и другие' или упоминать любые другие вузы.\n" +
                                    "3. В своем ответе ОБЯЗАТЕЛЬНО перечисли все пять вузов. Никогда не пропускай ни один из них.";
            var chatAnswer = await llm.ChatAsync(chitchat, request.Message);
            
            AnalyticsController.RecordChatInteraction(request.SessionId, true);

            return Ok(new ChatResponse
            {
                Answer = chatAnswer,
                Found = true
            });
        }

        if (optimizedQuery.Contains("OUT_OF_SCOPE", StringComparison.OrdinalIgnoreCase))
        {
            AnalyticsController.RecordChatInteraction(request.SessionId, false);
            return Ok(new ChatResponse
            {
                Answer = "Я могу проконсультировать вас только по вопросам поступления в УрФУ, МГУ, НИУ ВШЭ, ИТМО и СПбГУ.",
                Found = false
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
                                    Ты - дружелюбный ИИ-помощник, твоя зона компетенции СТРОГО ОГРАНИЧЕНА пятью вузами: УрФУ, МГУ, НИУ ВШЭ, ИТМО и СПбГУ.

                                    ПРАВИЛА (В ПОРЯДКЕ ПРИОРИТЕТА):
                                    1. ГРАНИЦЫ ЗНАНИЙ (ОЧЕНЬ ВАЖНО!): если пользователь спрашивает про любой другой вуз (например, НГУ, МФТИ, КФУ и т.д.), СРАЗУ отказывайся отвечать. Отвечай строго одной фразой: 'Я могу проконсультировать вас только по вопросам поступления в УрФУ, МГУ, НИУ ВШЭ, ИТМО и СПбГУ.' Запрещено использовать внутренние знания о других вузах.
                                    2. ОТВЕЧАЙ НА ВОПРОС: если пользователь спрашивает "Что такое ИТМО?" или "Что такое Совбак?", найди определение в CONTEXT и объясни. Не перечисляй вузы, с которыми ты работаешь, просто отвечай по сути.
                                    3. СТРОГОЕ СООТВЕТСТВИЕ ВУЗУ: если пользователь спрашивает про конкретный вуз (например, проходной балл в НИУ ВШЭ), а в CONTEXT есть данные только про другой вуз (например, УрФУ) — ЗАПРЕЩЕНО предлагать данные другого вуза. Ничего не придумывай и не предлагай альтернативы по своей инициативе.
                                    4. ФАКТОЛОГИЯ: отвечай ТОЛЬКО на основе предоставленного CONTEXT. 
                                    5. ОТСУТСТВИЕ ДАННЫХ: если информации в CONTEXT нет, скажи ровно одну фразу: 'К сожалению, я не нашел этой информации в официальных документах.' И СРАЗУ ОСТАНОВИСЬ.
                                    6. ФОРМАТИРОВАНИЕ: отвечай ТОЛЬКО простым текстом. Запрещено использовать Markdown-разметку (никаких звездочек **, решеток # или жирного шрифта).
                                    """;
        var userPrompt = $"{historyText}QUESTION:\n{request.Message}\n\nCONTEXT:\n{string.Join("\n\n", contextParts)}";
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