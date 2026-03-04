using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace UniHelper.Qdrant;

public class QdrantDataBase
{
    public static async Task IngestAsync(
        string jsonlPath,
        OpenAiEmbeddingClient embedder,
        QdrantClient qdrantClient,
        CancellationToken cancellationToken = default)
    {
        var probe = await embedder.EmbeddingAsync("probe", cancellationToken);
        await qdrantClient.CreateCollectionAsync(probe.Length, cancellationToken);
        
        const int batchSize = 32;
        var idBatch = new List<string>(batchSize);
        var textBatch = new List<string>(batchSize);
        var payloadBatch = new List<Dictionary<string, object>>(batchSize);

        foreach (var line in File.ReadLines(jsonlPath))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;
            
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            var sourceId = root.GetProperty("id").GetString() ?? Guid.NewGuid().ToString("N");
            var text = root.GetProperty("text").GetString() ?? "";
            var pointId = CreateDeterministicGuid(sourceId).ToString();

            if (string.IsNullOrWhiteSpace(text))
                continue;

            var payload = JsonSerializer.Deserialize<Dictionary<string, object>>(line) ?? new Dictionary<string, object>();
            payload["source_id"] = sourceId;
            
            idBatch.Add(pointId);
            textBatch.Add(text);
            payloadBatch.Add(payload);

            if (textBatch.Count >= batchSize)
                await FlushAsync();
        }

        if (textBatch.Count > 0)
            await FlushAsync();

        async Task FlushAsync()
        {
            var vectors = await embedder.EmbeddingBatchAsync(textBatch, cancellationToken);
            var points = new List<object>(textBatch.Count);
            
            for (var i = 0; i < textBatch.Count; i++)
                points.Add(new { id = idBatch[i], vector = vectors[i], payload = payloadBatch[i] });
            
            await qdrantClient.UpsertAsync(points, cancellationToken);
            
            idBatch.Clear();
            textBatch.Clear();
            payloadBatch.Clear();
        }

        static Guid CreateDeterministicGuid(string input)
        {
            var bytes = MD5.HashData(Encoding.UTF8.GetBytes(input));
            return new Guid(bytes);
        }
    }
}