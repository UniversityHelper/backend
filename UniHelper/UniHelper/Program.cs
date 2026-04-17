using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UniHelper.Qdrant;

namespace UniHelper;

public static class Program
{
    public static async Task Main(string[] args)
    {
        var config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .AddEnvironmentVariables()
            .Build();
        
        var qdrantUrl = config["Qdrant:Url"]!;
        var qdrantKey = config["Qdrant:ApiKey"]!;
        var collection = config["Qdrant:Collection"]!;
        var openAiKey = config["OpenAI:ApiKey"]!;
        var embeddingModel = config["OpenAI:EmbeddingModel"]!;
        var chatModel = config["OpenAI:ChatModel"]!;
        var proxyUrl = config["Proxy:ProxyUrl"]!;
        var login = config["Proxy:Login"]!;
        var password = config["Proxy:Password"]!;
        
        var webProxy = new WebProxy(proxyUrl)
        {
            Credentials = new NetworkCredential(login, password)
        };
        var handler = new HttpClientHandler
        {
            Proxy = webProxy,
            UseProxy = true
        };
        
        var httpClient = new HttpClient(handler);
        var embedder = new OpenAiEmbeddingClient(openAiKey, embeddingModel, httpClient);
        var qdrant = new QdrantClient(qdrantUrl, qdrantKey, collection);
        var llm = new OpenAiChatClient(openAiKey, chatModel, httpClient);
        
        if (args.Length == 0)
        {
            var builder = WebApplication.CreateBuilder(args);
            builder.Services.AddControllers();
            builder.Services.AddCors(options =>
            {
                options.AddDefaultPolicy(policy =>
                {
                    policy.AllowAnyOrigin()
                        .AllowAnyHeader()
                        .AllowAnyMethod();
                });
            });
            
            var app = builder.Build();
            app.UseCors();
            app.MapControllers();
            await app.RunAsync();
            
            return;
        }

        if (args[0].Equals("ingest", StringComparison.OrdinalIgnoreCase))
        {
            var paths = args.Skip(1).ToArray();
            if (paths.Length == 0)
                throw new ArgumentException("Usage: ingest <jsonl1> <jsonl2> ...");

            foreach (var path in paths)
            {
                Console.WriteLine($"Ingesting {path}");
                await QdrantDataBase.IngestAsync(path, embedder, qdrant);
            }
            
            Console.WriteLine("Ingestion completed");
            return;
        }

        if (args[0].Equals("ask", StringComparison.OrdinalIgnoreCase))
        {
            var question = args.Length >= 2 ? string.Join(' ', args.Skip(1)) : throw new ArgumentException("Missing question");
            var questionVector = await embedder.EmbeddingAsync(question);
            var hits = await qdrant.SearchAsync(questionVector, limit: 8);
            var contextLines = new List<string>();
            var i = 1;

            foreach (var hit in hits.EnumerateArray())
            {
                var payload = hit.GetProperty("payload");
                var text = payload.GetProperty("text").GetString() ?? "";
                var url = payload.TryGetProperty("url", out var urlObject) ? urlObject.GetString() ?? "" : "";
                
                contextLines.Add($"[{i}] url={url}\n{text}");
                i++;
            }
            
            const string system = "Отвечай только по CONTEXT. Всегда добавляй источники (url). " +
                                  "Если в CONTEXT нет ответа, скажи: \"Не найдено в официальных источниках УрФУ\". " +
                                  "Не придумывай числа и даты.";
            var user = $"QUESTION: {question}\n\nCONTEXT:\n{string.Join("\n\n", contextLines)}";
            var answer = await llm.ChatAsync(system, user);
            
            Console.WriteLine(answer);
            return;
        }
        
        Console.WriteLine("Unknown command");
    }
}