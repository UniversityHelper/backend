#pragma warning disable SKEXP0001
#pragma warning disable SKEXP0052
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.HuggingFace;
using Microsoft.SemanticKernel.Memory;
using UniversityHelper.Shared;

namespace UniversityHelper.ChatAPI;

public class RagChatService(ISemanticTextMemory memory, AiSettings aiSettings)
{
    public async Task<string> AskAsync(string question)
    {
        var searchResults = await memory.SearchAsync("university-info", question, limit: 8, minRelevanceScore: 0.3).ToListAsync();

        if (searchResults.Count == 0)
        {
            return "К сожалению, по вашему запросу информация не найдена в базе знаний.";
        }

        // Сортируем по релевантности и берём лучшие
        var context = string.Join("\n\n---\n\n",
            searchResults
                .OrderByDescending(r => r.Relevance)
                .Select(r => r.Metadata.Text));

        var kernel = Kernel.CreateBuilder()
            .AddHuggingFaceTextGeneration(aiSettings.ChatModel!, endpoint: new Uri(aiSettings.ChatEndpoint!), apiKey: aiSettings.ApiKey!)
            .Build();

        var prompt = $"""
            Ты помощник для абитуриентов УрФУ. Отвечай только на основе предоставленных данных.
            Если информации недостаточно — скажи об этом честно.
            Отвечай на русском языке, подробно и структурированно.
            
            Данные из базы знаний:
            {context}
            
            Вопрос: {question}
            
            Ответ:
            """;

        var result = await kernel.InvokePromptAsync(prompt);

        return result.GetValue<string>() ?? "";
    }
}

