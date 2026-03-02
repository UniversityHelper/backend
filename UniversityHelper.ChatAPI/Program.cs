#pragma warning disable SKEXP0001
#pragma warning disable SKEXP0020
#pragma warning disable SKEXP0011
#pragma warning disable SKEXP0050
using Microsoft.Extensions.AI;
using Microsoft.SemanticKernel.Connectors.Qdrant;
using Microsoft.SemanticKernel.Memory;
using UniversityHelper.ChatAPI;
using UniversityHelper.Shared;


var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);

var aiSettings = builder.Configuration.GetSection("AiSettings").Get<AiSettings>()
               ?? throw new InvalidOperationException("AiSettings not configured");

if (string.IsNullOrEmpty(aiSettings.ApiKey)) throw new InvalidOperationException("AiSettings:ApiKey is required");
if (string.IsNullOrEmpty(aiSettings.OnnxModelPath)) throw new InvalidOperationException("AiSettings:OnnxModelPath is required");
if (string.IsNullOrEmpty(aiSettings.QdrantEndpoint)) throw new InvalidOperationException("AiSettings:QdrantEndpoint is required");
if (string.IsNullOrEmpty(aiSettings.ChatEndpoint)) throw new InvalidOperationException("AiSettings:ChatEndpoint is required");
if (string.IsNullOrEmpty(aiSettings.ChatModel)) throw new InvalidOperationException("AiSettings:ChatModel is required");

builder.Services.AddSingleton(aiSettings);
builder.Services.AddHttpClient("HuggingFace", (sp, client) =>
{
    var settings = sp.GetRequiredService<AiSettings>();
    client.DefaultRequestHeaders.Add("Authorization", $"Bearer {settings.ApiKey!}");
});

builder.Services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(sp =>
{
    var settings = sp.GetRequiredService<AiSettings>();
    return new OnnxTextEmbeddingService(settings.OnnxModelPath!);
});


builder.Services.AddSingleton<ISemanticTextMemory>(sp =>
{
    var settings = sp.GetRequiredService<AiSettings>();
    var store = new QdrantMemoryStore(new HttpClient(), 384, settings.QdrantEndpoint!);
    var embeddingGenerator = sp.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>();
    
    return new SemanticTextMemory(store, embeddingGenerator);
});

builder.Services.AddSingleton<RagChatService>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.MapPost("/ask", async (AskPayload payload, RagChatService service) =>
    {
        var result = await service.AskAsync(payload.Question);
        return Results.Ok(new { Answer = result });
    })
    .WithName("Ask")
    .WithOpenApi();

await app.RunAsync();

public record AskPayload(string Question);
