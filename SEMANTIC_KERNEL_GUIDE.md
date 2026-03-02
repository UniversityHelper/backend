# Semantic Kernel с локальной ONNX моделью - Руководство для Copilot

Это руководство для использования в GitHub Copilot в JetBrains Rider для создания RAG-приложения с Semantic Kernel и локальной ONNX моделью.

---

## Архитектура решения

```
┌─────────────────────────────────────────────────────┐
│  Ваше .NET приложение (Semantic Kernel)             │
│  ┌──────────────────┐      ┌────────────────────┐  │
│  │ Text Embedding   │──────│  ONNX Runtime      │  │
│  │ Generation       │      │  (локальная модель)│  │
│  └──────────────────┘      └────────────────────┘  │
│           │                                          │
│           ▼                                          │
│  ┌──────────────────┐                               │
│  │ Memory Store     │──────── Qdrant               │
│  │ (Qdrant)         │         (http://localhost:6333)│
│  └──────────────────┘                               │
└─────────────────────────────────────────────────────┘
```

---

## Шаг 1: Экспорт модели в ONNX (Python, один раз)

Сначала нужно экспортировать вашу модель `all-MiniLM-L6-v2` в формат ONNX.

**Создайте файл `export_to_onnx.py`:**

```python
"""
Экспорт sentence-transformers модели в ONNX формат
Запустить один раз для подготовки модели для .NET
"""

from optimum.onnxruntime import ORTModelForFeatureExtraction
from transformers import AutoTokenizer
import os

MODEL_ID = "sentence-transformers/all-MiniLM-L6-v2"
OUTPUT_PATH = "./onnx-all-MiniLM-L6-v2"

print(f"Экспорт модели {MODEL_ID} в ONNX формат...")

# Экспорт модели
model = ORTModelForFeatureExtraction.from_pretrained(MODEL_ID, export=True)
tokenizer = AutoTokenizer.from_pretrained(MODEL_ID)

# Сохранение
os.makedirs(OUTPUT_PATH, exist_ok=True)
model.save_pretrained(OUTPUT_PATH)
tokenizer.save_pretrained(OUTPUT_PATH)

print(f"✅ Модель экспортирована в {OUTPUT_PATH}")
print(f"📁 Файлы:")
for file in os.listdir(OUTPUT_PATH):
    size = os.path.getsize(os.path.join(OUTPUT_PATH, file)) / (1024 * 1024)
    print(f"   - {file} ({size:.2f} MB)")
```

**Установите зависимости и запустите:**

```bash
pip install optimum[onnxruntime] transformers
python export_to_onnx.py
```

После этого у вас будет папка `onnx-all-MiniLM-L6-v2` с файлами модели.

---

## Шаг 2: Создание .NET проекта

**Скажите Copilot в Rider:**

```
Создай новый консольный .NET проект с именем "RagWithSemanticKernel" (.NET 8.0).
Добавь следующие NuGet пакеты:
- Microsoft.SemanticKernel (версия 1.0.0 или новее)
- Microsoft.ML.OnnxRuntime (версия 1.17.0 или новее)
- Microsoft.ML.Tokenizers (prerelease версия 0.21.0 или новее)
- System.Text.Json
```

**Или вручную в терминале:**

```bash
dotnet new console -n RagWithSemanticKernel
cd RagWithSemanticKernel

dotnet add package Microsoft.SemanticKernel
dotnet add package Microsoft.ML.OnnxRuntime
dotnet add package Microsoft.ML.Tokenizers --prerelease
```

---

## Шаг 3: Реализация ONNX Text Embedding сервиса

**Скажите Copilot:**

```
Создай класс OnnxTextEmbeddingService, который:
1. Реализует интерфейс ITextEmbeddingGenerationService из Semantic Kernel
2. Использует Microsoft.ML.OnnxRuntime для загрузки ONNX модели из папки
3. Использует Microsoft.ML.Tokenizers для токенизации текста
4. Реализует метод GenerateEmbeddingsAsync для генерации эмбеддингов
5. Выполняет mean pooling и L2 нормализацию векторов
6. Поддерживает батчинг (обработка нескольких текстов за раз)

Параметры конструктора:
- string modelPath: путь к папке с model.onnx и tokenizer.json
- int maxLength: максимальная длина последовательности (по умолчанию 256)

Модель имеет следующие входы:
- input_ids: long[batch_size, sequence_length]
- attention_mask: long[batch_size, sequence_length]
- token_type_ids: long[batch_size, sequence_length]

Выход модели:
- last_hidden_state: float[batch_size, sequence_length, hidden_size]

Нужно:
1. Токенизировать текст
2. Создать padding до maxLength
3. Запустить ONNX inference
4. Применить mean pooling (учитывая attention_mask)
5. Нормализовать L2 norm
6. Вернуть ReadOnlyMemory<float> для каждого текста
```

**Готовый код (покажите Copilot как пример):**

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.ML.Tokenizers;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Embeddings;

public class OnnxTextEmbeddingService : ITextEmbeddingGenerationService
{
    private readonly InferenceSession _session;
    private readonly Tokenizer _tokenizer;
    private readonly int _maxLength;
    private readonly int _embeddingDimension;

    public IReadOnlyDictionary<string, object?> Attributes => new Dictionary<string, object?>();

    public OnnxTextEmbeddingService(string modelPath, int maxLength = 256)
    {
        var onnxPath = Path.Combine(modelPath, "model.onnx");
        var tokenizerPath = Path.Combine(modelPath, "tokenizer.json");

        if (!File.Exists(onnxPath))
            throw new FileNotFoundException($"ONNX model not found: {onnxPath}");
        if (!File.Exists(tokenizerPath))
            throw new FileNotFoundException($"Tokenizer not found: {tokenizerPath}");

        _session = new InferenceSession(onnxPath);
        _tokenizer = Tokenizer.CreateTokenizer(tokenizerPath);
        _maxLength = maxLength;

        // Определяем размерность эмбеддинга из метаданных модели
        var outputMeta = _session.OutputMetadata.First().Value;
        _embeddingDimension = outputMeta.Dimensions.LastOrDefault();
        if (_embeddingDimension <= 0) _embeddingDimension = 384; // для all-MiniLM-L6-v2

        Console.WriteLine($"✅ ONNX модель загружена: {onnxPath}");
        Console.WriteLine($"✅ Размерность эмбеддинга: {_embeddingDimension}");
    }

    public async Task<IList<ReadOnlyMemory<float>>> GenerateEmbeddingsAsync(
        IList<string> data,
        Kernel? kernel = null,
        CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            var batchSize = data.Count;
            
            // Токенизация
            var encodings = data.Select(text => _tokenizer.Encode(text)).ToArray();

            // Подготовка тензоров
            var inputIds = new long[batchSize, _maxLength];
            var attentionMask = new long[batchSize, _maxLength];
            var tokenTypeIds = new long[batchSize, _maxLength];

            for (int i = 0; i < batchSize; i++)
            {
                var ids = encodings[i].Ids.Take(_maxLength).ToArray();
                var len = ids.Length;

                for (int j = 0; j < _maxLength; j++)
                {
                    if (j < len)
                    {
                        inputIds[i, j] = ids[j];
                        attentionMask[i, j] = 1;
                        tokenTypeIds[i, j] = 0;
                    }
                    else
                    {
                        inputIds[i, j] = 0;
                        attentionMask[i, j] = 0;
                        tokenTypeIds[i, j] = 0;
                    }
                }
            }

            // Создание ONNX тензоров
            var inputIdsTensor = new DenseTensor<long>(inputIds, new[] { batchSize, _maxLength });
            var attentionMaskTensor = new DenseTensor<long>(attentionMask, new[] { batchSize, _maxLength });
            var tokenTypeIdsTensor = new DenseTensor<long>(tokenTypeIds, new[] { batchSize, _maxLength });

            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("input_ids", inputIdsTensor),
                NamedOnnxValue.CreateFromTensor("attention_mask", attentionMaskTensor),
                NamedOnnxValue.CreateFromTensor("token_type_ids", tokenTypeIdsTensor)
            };

            // Inference
            using var results = _session.Run(inputs);
            var lastHiddenState = results.First().AsTensor<float>();

            // Mean pooling + нормализация
            var embeddings = new List<ReadOnlyMemory<float>>();
            for (int i = 0; i < batchSize; i++)
            {
                var embedding = MeanPoolingAndNormalize(lastHiddenState, i, attentionMask);
                embeddings.Add(new ReadOnlyMemory<float>(embedding));
            }

            return embeddings;
        }, cancellationToken);
    }

    private float[] MeanPoolingAndNormalize(Tensor<float> hiddenStates, int batchIndex, long[,] attentionMask)
    {
        var seqLength = hiddenStates.Dimensions[1];
        var embeddingDim = hiddenStates.Dimensions[2];
        var result = new float[embeddingDim];
        var validTokens = 0;

        // Mean pooling
        for (int seqPos = 0; seqPos < seqLength; seqPos++)
        {
            if (attentionMask[batchIndex, seqPos] == 1)
            {
                for (int dim = 0; dim < embeddingDim; dim++)
                {
                    result[dim] += hiddenStates[batchIndex, seqPos, dim];
                }
                validTokens++;
            }
        }

        // Усреднение
        if (validTokens > 0)
        {
            for (int dim = 0; dim < embeddingDim; dim++)
            {
                result[dim] /= validTokens;
            }
        }

        // L2 нормализация
        var norm = Math.Sqrt(result.Sum(x => x * x));
        if (norm > 0)
        {
            for (int i = 0; i < embeddingDim; i++)
            {
                result[i] /= (float)norm;
            }
        }

        return result;
    }

    public void Dispose()
    {
        _session?.Dispose();
    }
}
```

---

## Шаг 4: Реализация Qdrant Memory Store

**Скажите Copilot:**

```
Создай класс QdrantMemoryStore, который:
1. Реализует интерфейс IMemoryStore из Semantic Kernel
2. Использует HttpClient для взаимодействия с Qdrant REST API
3. Реализует методы:
   - CreateCollectionAsync: создает коллекцию в Qdrant
   - UpsertAsync: добавляет/обновляет записи
   - GetAsync: получает запись по ID
   - SearchAsync: поиск похожих векторов
   - DeleteAsync: удаляет запись
   - GetCollectionsAsync: список коллекций

Qdrant API endpoints:
- PUT /collections/{name} - создание коллекции
- PUT /collections/{name}/points - upsert точек
- POST /collections/{name}/points/search - поиск
- GET /collections/{name}/points/{id} - получение точки
- DELETE /collections/{name}/points - удаление точек

Используй System.Text.Json для сериализации.
```

**Готовый код:**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.SemanticKernel.Memory;

public class QdrantMemoryStore : IMemoryStore
{
    private readonly HttpClient _httpClient;
    private readonly int _vectorSize;

    public QdrantMemoryStore(string qdrantUrl, int vectorSize)
    {
        _httpClient = new HttpClient { BaseAddress = new Uri(qdrantUrl) };
        _vectorSize = vectorSize;
    }

    public async Task CreateCollectionAsync(string collectionName, CancellationToken cancellationToken = default)
    {
        var request = new
        {
            vectors = new
            {
                size = _vectorSize,
                distance = "Cosine"
            }
        };

        var json = JsonSerializer.Serialize(request);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        try
        {
            var response = await _httpClient.PutAsync($"/collections/{collectionName}", content, cancellationToken);
            // Игнорируем ошибку если коллекция уже существует
        }
        catch { }
    }

    public async Task<string> UpsertAsync(string collectionName, MemoryRecord record, CancellationToken cancellationToken = default)
    {
        var point = new
        {
            points = new[]
            {
                new
                {
                    id = record.Metadata.Id,
                    vector = record.Embedding.ToArray(),
                    payload = new
                    {
                        text = record.Metadata.Text,
                        description = record.Metadata.Description,
                        additional_metadata = record.Metadata.AdditionalMetadata,
                        external_source_name = record.Metadata.ExternalSourceName,
                        id = record.Metadata.Id
                    }
                }
            }
        };

        var json = JsonSerializer.Serialize(point);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _httpClient.PutAsync($"/collections/{collectionName}/points", content, cancellationToken);
        response.EnsureSuccessStatusCode();

        return record.Metadata.Id;
    }

    public async IAsyncEnumerable<string> UpsertBatchAsync(
        string collectionName,
        IEnumerable<MemoryRecord> records,
        CancellationToken cancellationToken = default)
    {
        var recordsList = records.ToList();
        
        var points = recordsList.Select(record => new
        {
            id = record.Metadata.Id,
            vector = record.Embedding.ToArray(),
            payload = new
            {
                text = record.Metadata.Text,
                description = record.Metadata.Description,
                additional_metadata = record.Metadata.AdditionalMetadata,
                external_source_name = record.Metadata.ExternalSourceName,
                id = record.Metadata.Id
            }
        }).ToArray();

        var request = new { points = points };
        var json = JsonSerializer.Serialize(request);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _httpClient.PutAsync($"/collections/{collectionName}/points", content, cancellationToken);
        response.EnsureSuccessStatusCode();

        foreach (var record in recordsList)
        {
            yield return record.Metadata.Id;
        }
    }

    public async Task<MemoryRecord?> GetAsync(string collectionName, string key, bool withEmbedding = false, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/collections/{collectionName}/points/{key}", cancellationToken);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var result = JsonSerializer.Deserialize<JsonElement>(json);
            var point = result.GetProperty("result");

            var payload = point.GetProperty("payload");
            var metadata = new MemoryRecordMetadata(
                isReference: false,
                id: payload.GetProperty("id").GetString()!,
                text: payload.GetProperty("text").GetString()!,
                description: payload.GetProperty("description").GetString()!,
                externalSourceName: payload.TryGetProperty("external_source_name", out var esn) ? esn.GetString()! : "",
                additionalMetadata: payload.TryGetProperty("additional_metadata", out var am) ? am.GetString()! : ""
            );

            ReadOnlyMemory<float> embedding = ReadOnlyMemory<float>.Empty;
            if (withEmbedding && point.TryGetProperty("vector", out var vectorElement))
            {
                var vector = vectorElement.EnumerateArray().Select(e => e.GetSingle()).ToArray();
                embedding = new ReadOnlyMemory<float>(vector);
            }

            return new MemoryRecord(metadata, embedding, null);
        }
        catch
        {
            return null;
        }
    }

    public async IAsyncEnumerable<MemoryRecord> GetBatchAsync(
        string collectionName,
        IEnumerable<string> keys,
        bool withEmbeddings = false,
        CancellationToken cancellationToken = default)
    {
        foreach (var key in keys)
        {
            var record = await GetAsync(collectionName, key, withEmbeddings, cancellationToken);
            if (record != null)
            {
                yield return record;
            }
        }
    }

    public Task RemoveAsync(string collectionName, string key, CancellationToken cancellationToken = default)
    {
        // Qdrant требует POST запрос для удаления
        return Task.CompletedTask; // Упрощенная реализация
    }

    public Task RemoveBatchAsync(string collectionName, IEnumerable<string> keys, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask; // Упрощенная реализация
    }

    public async IAsyncEnumerable<(MemoryRecord, double)> GetNearestMatchesAsync(
        string collectionName,
        ReadOnlyMemory<float> embedding,
        int limit,
        double minRelevanceScore = 0,
        bool withEmbeddings = false,
        CancellationToken cancellationToken = default)
    {
        var searchRequest = new
        {
            vector = embedding.ToArray(),
            top = limit,
            with_payload = true,
            with_vector = withEmbeddings
        };

        var json = JsonSerializer.Serialize(searchRequest);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync($"/collections/{collectionName}/points/search", content, cancellationToken);
        response.EnsureSuccessStatusCode();

        var resultJson = await response.Content.ReadAsStringAsync(cancellationToken);
        var result = JsonSerializer.Deserialize<JsonElement>(resultJson);
        var hits = result.GetProperty("result").EnumerateArray();

        foreach (var hit in hits)
        {
            var score = hit.GetProperty("score").GetDouble();
            if (score < minRelevanceScore) continue;

            var payload = hit.GetProperty("payload");
            var metadata = new MemoryRecordMetadata(
                isReference: false,
                id: payload.GetProperty("id").GetString()!,
                text: payload.GetProperty("text").GetString()!,
                description: payload.GetProperty("description").GetString()!,
                externalSourceName: payload.TryGetProperty("external_source_name", out var esn) ? esn.GetString()! : "",
                additionalMetadata: payload.TryGetProperty("additional_metadata", out var am) ? am.GetString()! : ""
            );

            ReadOnlyMemory<float> vectorEmbedding = ReadOnlyMemory<float>.Empty;
            if (withEmbeddings && hit.TryGetProperty("vector", out var vectorElement))
            {
                var vector = vectorElement.EnumerateArray().Select(e => e.GetSingle()).ToArray();
                vectorEmbedding = new ReadOnlyMemory<float>(vector);
            }

            var record = new MemoryRecord(metadata, vectorEmbedding, null);
            yield return (record, score);
        }
    }

    public async Task<(MemoryRecord, double)?> GetNearestMatchAsync(
        string collectionName,
        ReadOnlyMemory<float> embedding,
        double minRelevanceScore = 0,
        bool withEmbedding = false,
        CancellationToken cancellationToken = default)
    {
        await foreach (var match in GetNearestMatchesAsync(collectionName, embedding, 1, minRelevanceScore, withEmbedding, cancellationToken))
        {
            return match;
        }
        return null;
    }

    public async IAsyncEnumerable<string> GetCollectionsAsync(CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync("/collections", cancellationToken);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var result = JsonSerializer.Deserialize<JsonElement>(json);
        var collections = result.GetProperty("result").GetProperty("collections").EnumerateArray();

        foreach (var collection in collections)
        {
            yield return collection.GetProperty("name").GetString()!;
        }
    }

    public async Task DeleteCollectionAsync(string collectionName, CancellationToken cancellationToken = default)
    {
        await _httpClient.DeleteAsync($"/collections/{collectionName}", cancellationToken);
    }

    public Task<bool> DoesCollectionExistAsync(string collectionName, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(true); // Упрощенная реализация
    }
}
```

---

## Шаг 5: Основное приложение с Semantic Kernel

**Скажите Copilot:**

```
Создай класс Program с методом Main, который:

1. Создает Kernel с использованием:
   - OnnxTextEmbeddingService (локальная ONNX модель)
   - QdrantMemoryStore (векторная БД)

2. Создает ISemanticTextMemory для работы с памятью

3. Реализует функцию индексации документа:
   - Принимает текст и URL источника
   - Разбивает текст на чанки по 1000 символов с перекрытием 200
   - Сохраняет каждый чанк в память через SaveInformationAsync

4. Реализует функцию поиска:
   - Принимает вопрос пользователя
   - Ищет релевантные чанки через SearchAsync
   - Выводит топ-5 результатов с score

5. Интерактивный режим:
   - Ввод вопроса
   - Поиск ответа
   - Вывод результатов

Параметры:
- Путь к ONNX модели: ./onnx-all-MiniLM-L6-v2
- Qdrant URL: http://localhost:6333
- Размер вектора: 384
- Коллекция: urfu_programs
```

**Готовый код:**

```csharp
using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Memory;

class Program
{
    const string ONNX_MODEL_PATH = @"C:\Users\anton\PycharmProjects\PythonProject3\onnx-all-MiniLM-L6-v2";
    const string QDRANT_URL = "http://localhost:6333";
    const int VECTOR_SIZE = 384;
    const string COLLECTION_NAME = "urfu_programs";

    static async Task Main(string[] args)
    {
        Console.WriteLine("🚀 Semantic Kernel + ONNX + Qdrant RAG Demo");
        Console.WriteLine("=" + new string('=', 60));

        // 1. Создание Kernel с локальной ONNX моделью
        var embeddingService = new OnnxTextEmbeddingService(ONNX_MODEL_PATH);
        
        var kernelBuilder = Kernel.CreateBuilder();
        kernelBuilder.Services.AddSingleton<ITextEmbeddingGenerationService>(embeddingService);
        var kernel = kernelBuilder.Build();

        // 2. Создание Memory Store (Qdrant)
        var memoryStore = new QdrantMemoryStore(QDRANT_URL, VECTOR_SIZE);
        await memoryStore.CreateCollectionAsync(COLLECTION_NAME);

        // 3. Создание Semantic Memory
        var memory = new MemoryBuilder()
            .WithMemoryStore(memoryStore)
            .WithTextEmbeddingGeneration(embeddingService)
            .Build();

        Console.WriteLine($"✅ Kernel инициализирован");
        Console.WriteLine($"✅ Подключено к Qdrant: {QDRANT_URL}");
        Console.WriteLine($"✅ Коллекция: {COLLECTION_NAME}");
        Console.WriteLine();

        // 4. Меню
        while (true)
        {
            Console.WriteLine("\nВыберите действие:");
            Console.WriteLine("1 - Индексировать документ");
            Console.WriteLine("2 - Поиск по вопросу");
            Console.WriteLine("3 - Выход");
            Console.Write("Ваш выбор: ");

            var choice = Console.ReadLine();

            switch (choice)
            {
                case "1":
                    await IndexDocumentAsync(memory);
                    break;
                case "2":
                    await SearchAsync(memory);
                    break;
                case "3":
                    Console.WriteLine("До свидания!");
                    return;
                default:
                    Console.WriteLine("Неверный выбор");
                    break;
            }
        }
    }

    static async Task IndexDocumentAsync(ISemanticTextMemory memory)
    {
        Console.Write("\nВведите URL источника: ");
        var url = Console.ReadLine() ?? "https://example.com";

        Console.Write("Введите текст документа (или путь к файлу): ");
        var input = Console.ReadLine() ?? "";

        string text;
        if (System.IO.File.Exists(input))
        {
            text = await System.IO.File.ReadAllTextAsync(input);
            Console.WriteLine($"📄 Загружен файл: {input} ({text.Length} символов)");
        }
        else
        {
            text = input;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            Console.WriteLine("❌ Текст пустой");
            return;
        }

        // Разбивка на чанки
        var chunks = ChunkText(text, chunkSize: 1000, overlap: 200);
        Console.WriteLine($"📝 Разбито на {chunks.Count} чанков");

        // Индексация
        Console.WriteLine("🔄 Индексация...");
        for (int i = 0; i < chunks.Count; i++)
        {
            await memory.SaveInformationAsync(
                collection: COLLECTION_NAME,
                text: chunks[i],
                id: Guid.NewGuid().ToString(),
                description: $"Чанк {i + 1} из {chunks.Count}",
                additionalMetadata: $"source={url}&chunk_index={i}"
            );

            if ((i + 1) % 10 == 0 || i == chunks.Count - 1)
            {
                Console.WriteLine($"   Обработано: {i + 1}/{chunks.Count}");
            }
        }

        Console.WriteLine("✅ Индексация завершена!");
    }

    static async Task SearchAsync(ISemanticTextMemory memory)
    {
        Console.Write("\n❓ Введите вопрос: ");
        var query = Console.ReadLine();

        if (string.IsNullOrWhiteSpace(query))
        {
            Console.WriteLine("❌ Вопрос пустой");
            return;
        }

        Console.WriteLine("\n🔍 Поиск...");

        var results = memory.SearchAsync(
            collection: COLLECTION_NAME,
            query: query,
            limit: 5,
            minRelevanceScore: 0.0
        );

        Console.WriteLine("\n📊 Результаты:\n");

        int count = 0;
        await foreach (var result in results)
        {
            count++;
            Console.WriteLine($"[{count}] Релевантность: {result.Relevance:F4}");
            Console.WriteLine($"    Описание: {result.Metadata.Description}");
            Console.WriteLine($"    Метаданные: {result.Metadata.AdditionalMetadata}");
            Console.WriteLine($"    Текст: {result.Metadata.Text.Substring(0, Math.Min(200, result.Metadata.Text.Length))}...");
            Console.WriteLine();
        }

        if (count == 0)
        {
            Console.WriteLine("❌ Ничего не найдено. Проиндексируйте документы сначала.");
        }
    }

    static List<string> ChunkText(string text, int chunkSize, int overlap)
    {
        var chunks = new List<string>();
        int start = 0;

        while (start < text.Length)
        {
            int end = Math.Min(start + chunkSize, text.Length);
            var chunk = text.Substring(start, end - start).Trim();

            if (!string.IsNullOrWhiteSpace(chunk))
            {
                chunks.Add(chunk);
            }

            if (end == text.Length) break;
            start = end - overlap;
        }

        return chunks;
    }
}
```

---

## Что попросить у Copilot в Rider - Пошаговый план

### Шаг 1: Создание проекта

```
Создай консольное приложение .NET 8.0 с именем RagWithSemanticKernel
```

### Шаг 2: Установка пакетов

```
Добавь NuGet пакеты:
- Microsoft.SemanticKernel
- Microsoft.ML.OnnxRuntime  
- Microsoft.ML.Tokenizers (prerelease)
```

### Шаг 3: Класс OnnxTextEmbeddingService

```
Создай класс OnnxTextEmbeddingService.cs, который реализует ITextEmbeddingGenerationService.
Класс должен:
- Загружать ONNX модель из папки (model.onnx)
- Загружать токенизатор (tokenizer.json) через Microsoft.ML.Tokenizers
- Реализовать GenerateEmbeddingsAsync для генерации эмбеддингов
- Применять mean pooling и L2 нормализацию

Модель принимает: input_ids, attention_mask, token_type_ids (long tensors)
Модель возвращает: last_hidden_state (float tensor)

Используй код из файла DOTNET_EMBEDDING_ALTERNATIVES.md как референс.
```

### Шаг 4: Класс QdrantMemoryStore

```
Создай класс QdrantMemoryStore.cs, который реализует IMemoryStore.
Класс должен:
- Использовать HttpClient для REST API Qdrant
- Реализовать CreateCollectionAsync, UpsertAsync, UpsertBatchAsync, GetNearestMatchesAsync
- Использовать JSON сериализацию для запросов

Qdrant endpoints:
- PUT /collections/{name} - создание
- PUT /collections/{name}/points - upsert
- POST /collections/{name}/points/search - поиск
```

### Шаг 5: Основное приложение

```
В Program.cs создай:
1. Инициализацию Kernel с OnnxTextEmbeddingService
2. Создание MemoryBuilder с QdrantMemoryStore
3. Функцию IndexDocumentAsync - разбивает текст на чанки и сохраняет в память
4. Функцию SearchAsync - ищет по вопросу и выводит топ-5 результатов
5. Интерактивное меню (1-индексация, 2-поиск, 3-выход)

Параметры:
- ONNX модель: ./onnx-all-MiniLM-L6-v2
- Qdrant: http://localhost:6333
- Размер вектора: 384
- Коллекция: urfu_programs
```

---

## Запуск приложения

1. **Экспортируйте модель в ONNX** (если еще не сделали):
   ```bash
   python export_to_onnx.py
   ```

2. **Скопируйте папку с моделью** в проект или укажите абсолютный путь в Program.cs

3. **Запустите Qdrant**:
   ```bash
   docker run -p 6333:6333 qdrant/qdrant
   ```

4. **Запустите приложение**:
   ```bash
   dotnet run
   ```

---

## Альтернатива: Semantic Kernel Memory Connectors

Если не хотите писать QdrantMemoryStore вручную, можете попробовать:

```bash
dotnet add package Microsoft.SemanticKernel.Connectors.Qdrant --prerelease
```

Но на момент 2026 M03 этот пакет может быть в preview или недоступен. В таком случае используйте кастомную реализацию выше.

---

## Итоговая структура проекта

```
RagWithSemanticKernel/
├── Program.cs (главная логика)
├── OnnxTextEmbeddingService.cs (ONNX inference)
├── QdrantMemoryStore.cs (Qdrant интеграция)
├── RagWithSemanticKernel.csproj
└── onnx-all-MiniLM-L6-v2/ (экспортированная модель)
    ├── model.onnx
    ├── tokenizer.json
    └── config.json
```

---

## Преимущества этого подхода

✅ **Полностью .NET** - никакого Python в runtime  
✅ **Semantic Kernel** - высокоуровневый фреймворк от Microsoft  
✅ **Локальная модель** - без API токенов и интернета  
✅ **Production-ready** - легко масштабируется  
✅ **Типобезопасность** - все преимущества C#  

---

Скопируйте это руководство в Copilot Chat в Rider и попросите реализовать шаг за шагом! 🚀

