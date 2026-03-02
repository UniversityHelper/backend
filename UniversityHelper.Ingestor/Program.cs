#pragma warning disable SKEXP0001 
#pragma warning disable SKEXP0050
#pragma warning disable SKEXP0020
using Microsoft.Extensions.AI;
using Microsoft.SemanticKernel.Connectors.Qdrant;
using Microsoft.SemanticKernel.Memory;
using UniversityHelper.Ingestor;
using UniversityHelper.Shared;

var builder = Host.CreateApplicationBuilder(args);

builder.Configuration.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);

var aiSettings = builder.Configuration.GetSection("AiSettings").Get<AiSettings>() 
               ?? throw new InvalidOperationException("AiSettings not configured");
builder.Services.AddSingleton(aiSettings);

// Validate required settings for Ingestor
if (string.IsNullOrEmpty(aiSettings.OnnxModelPath))
    throw new InvalidOperationException("AiSettings:OnnxModelPath is required");
if (string.IsNullOrEmpty(aiSettings.QdrantEndpoint))
    throw new InvalidOperationException("AiSettings:QdrantEndpoint is required");

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

builder.Services.AddSingleton<ScraperService>();
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
await host.RunAsync();
