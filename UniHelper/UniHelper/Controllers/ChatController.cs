using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
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
            ? "КОНТЕКСТ ДИАЛОГА:\n" + string.Join('\n', request.History.Select(h => $"{h.Role}: {h.Content}")) + "\n"
            : "";
        var tools = new object[]
        {
            new
            {
                type = "function",
                function = new
                {
                    name = "search_university_database",
                    description = "Поиск официальной информации о направлениях, проходных баллах, местах и условиях.",
                    parameters = new
                    {
                        type = "object",
                        properties = new
                        {
                            searchQuery = new
                            {
                                type = "string",
                                description =
                                    "Точный поисковый запрос. ОБЯЗАТЕЛЬНО переводи аббревиатуры в верхний регистр или раскрывай их (например: 'фиит' -> 'ФИИТ' или 'Фундаментальная информатика')"
                            },
                            university = new
                            {
                                type = "string",
                                description =
                                    "Строго одно из значений если упомянуто: 'НИУ ВШЭ', 'МГУ', 'УрФУ', 'ИТМО', 'СПбГУ'"
                            },
                            level = new
                            {
                                type = "string",
                                description = "Уровень образования если упомянут: 'бакалавриат' или 'магистратура'"
                            }
                        },
                        required = new[] { "searchQuery" }
                    }
                }
            }
        };
        
        const string librarianPrompt = """
                                       Ты - библиотекарь-навигатор приемной комиссии. Твоя задача - извлечь параметры вопроса и найти информацию с помощью инструмента.
                                       ПРАВИЛА:
                                       1. ПАМЯТЬ: обязательно используй КОНТЕКСТ ДИАЛОГА. Если пользователь пишет "а какой там проходной?", пойми из контекста, о каком вузе и направлении идет речь.
                                       2. АББРЕВИАТУРЫ: распознавай аббревиатуры даже в нижнем регистре (фиит -> ФИИТ, пми -> ПМИ) и используй их в поиске.
                                       Твой поиск ограничен 5 вузами: МГУ, НИУ ВШЭ, УрФУ, ИТМО, СПбГУ.
                                       """;
        var librarianUserMessage = $"{historyText}QUESTION:\n{request.Message}";
        var messageResponse = await llm.ChatWithToolsAsync(librarianPrompt, librarianUserMessage, tools);
        List<string> contextParts = [];

        if (messageResponse.TryGetProperty("tool_calls", out var toolCalls) && toolCalls.GetArrayLength() > 0)
        {
            var firstCall = toolCalls[0].GetProperty("function");
            var argsJson = firstCall.GetProperty("arguments").GetString()!;
            using var argsDoc = JsonDocument.Parse(argsJson);
            var args = argsDoc.RootElement;
            var searchQuery = args.GetProperty("searchQuery").GetString()!;
            var targetUniversity = args.TryGetProperty("university", out var university) ? university.GetString() : null;
            var targetLevel = args.TryGetProperty("level", out var level) ? level.GetString() : null;
            var queryVector = await embedder.EmbeddingAsync(searchQuery);
            var searchHits = await qdrant.SearchAsync(queryVector, limit: 5, university: targetUniversity, level: targetLevel);

            foreach (var hit in searchHits.EnumerateArray())
                if (hit.TryGetProperty("payload", out var payload) && payload.TryGetProperty("text", out var text))
                    contextParts.Add(text.GetString() ?? "");
        }
        const string finalSystemPrompt = """
                                         Ты - робот-помощник UniHelper. Твоя область знаний СТРОГО ограничена пятью вузами: МГУ, НИУ ВШЭ, УрФУ, ИТМО и СПбГУ.

                                         ПРАВИЛА:
                                         1. СМОЛТОК И КОМПЕТЕНЦИЯ (ВАЖНО!): 
                                            - Если пользователь просто здоровается (привет, здравствуйте), прощается или благодарит — отвечай вежливо (например, "Здравствуйте! Чем я могу помочь?") без использования контекста.
                                            - Если пользователь спрашивает, какие вузы ты знаешь, с какими университетами работаешь или какова твоя область знаний — четко перечисли только эти пять вузов: МГУ, НИУ ВШЭ, УрФУ, ИТМО и СПбГУ. Строго запрещено говорить "и другие" или упоминать сторонние вузы.
                                            - При обработке приветствий и вопросов о твоих возможностях никогда не пиши про отсутствие данных в документах.
                                         2. ОТСУТСТВИЕ ДАННЫХ: если задан конкретный вопрос по вузу/поступлению (например, про проходные баллы, направления, общежития), но в CONTEXT нет ответа, скажи ровно одну фразу: 'К сожалению, я не нашел этой информации в официальных документах.' И СРАЗУ ОСТАНОВИСЬ.
                                         3. ФАКТОЛОГИЯ: отвечай ТОЛЬКО на основе предоставленного CONTEXT. Ничего не придумывай.
                                         4. ФОРМАТИРОВАНИЕ: отвечай ТОЛЬКО простым текстом без Markdown разметки.
                                         """;
        var finalUserPrompt = $"{historyText}QUESTION:\n{request.Message}\n\nCONTEXT:\n{string.Join("\n\n", contextParts)}";
        var answer = await llm.ChatAsync(finalSystemPrompt, finalUserPrompt);
        var isFound = !answer.Contains("К сожалению, я не нашел");
        
        AnalyticsController.RecordChatInteraction(request.SessionId, isFound);
        return Ok(new ChatResponse
        {
            Answer = answer, 
            Found = isFound
        });
    }
}