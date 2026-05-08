using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
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
        
        if (args.Length == 0)
        {
            var builder = WebApplication.CreateBuilder(args);
            builder.Services.AddControllers();
            
            var myAllowSpecificOrigins = "UniHelperPolicy";
            builder.Services.AddCors(options =>
            {
                options.AddPolicy(name: myAllowSpecificOrigins,
                    policy =>
                    {
                        policy.WithOrigins("https://gunihelper.space") 
                            .AllowAnyHeader()
                            .AllowAnyMethod();
                    });
            });

            builder.Services.AddHttpClient("OpenAI", _ => {}).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                Proxy = new WebProxy(proxyUrl)
                {
                    Credentials = new NetworkCredential(login, password)
                }, 
                UseProxy = true
            });

            builder.Services.AddSingleton<OpenAiEmbeddingClient>(sp =>
            {
                var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient("OpenAI");
                return new OpenAiEmbeddingClient(openAiKey, embeddingModel, httpClient);
            });
            
            builder.Services.AddSingleton<OpenAiChatClient>(sp =>
            {
                var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient("OpenAI");
                return new OpenAiChatClient(openAiKey, chatModel, httpClient);
            });
            
            builder.Services.AddSingleton<QdrantClient>(sp => new QdrantClient(qdrantUrl, qdrantKey, collection));
            
            builder.WebHost.UseUrls("http://0.0.0.0:5000");

            var app = builder.Build();
            app.UseRouting();
            app.UseCors(myAllowSpecificOrigins);
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
        
        Console.WriteLine("Unknown command");
    }
}