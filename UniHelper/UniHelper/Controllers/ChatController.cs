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
        const string rewritePrompt = "Ты - AI, который превращает неточные вопросы пользователей в идеальные поисковые запросы для векторной базы данных. " +
                                     "ОБЯЗАТЕЛЬНО используй КОНТЕКСТ ПРЕДЫДУЩЕГО ДИАЛОГА, чтобы понять, о каком вузе или направлении идет речь, если текущий вопрос короткий (например, 'какой проходной?' -> 'какой проходной балл ВШЭ Совбак'). " + 
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
                                    Ты - дружелюбный ИИ-помощник Уральского федерального университета (УрФУ), Высшей школы экономики (НИУ ВШЭ), московского государственного университета (МГУ), Санкт-Петербургский государственный университет (СПбГУ), ИТМО. 
                                    Твоя задача - помогать абитуриентам и студентам.

                                    ПРАВИЛА (В ПОРЯДКЕ ПРИОРИТЕТА):
                                    1. ОТВЕЧАЙ НА ВОПРОС: если пользователь спрашивает "Что такое ИТМО?" или "Что такое Совбак?", найди определение в CONTEXT и объясни. Не перечисляй вузы, с которыми ты работаешь, просто отвечай по сути.
                                    2. ЗАПРЕТ НАВЯЗЫВАНИЯ: НИКОГДА не предлагай альтернативные вузы или направления по своей инициативе. Если пользователь спрашивает про ВШЭ, говори ТОЛЬКО про ВШЭ. Строго запрещено говорить: "У меня нет данных, но рассмотрите УрФУ...".
                                    3. ФАКТОЛОГИЯ: Отвечай ТОЛЬКО на основе предоставленного CONTEXT. 
                                    4. ОТСУТСТВИЕ ДАННЫХ: если информации в CONTEXT нет, скажи ровно одну фразу: 'К сожалению, я не нашел этой информации в официальных документах.' И СРАЗУ ОСТАНОВИСЬ.
                                    5. ФОРМАТИРОВАНИЕ: отвечай ТОЛЬКО простым текстом. Запрещено использовать Markdown-разметку (никаких звездочек **, решеток # или жирного шрифта).
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