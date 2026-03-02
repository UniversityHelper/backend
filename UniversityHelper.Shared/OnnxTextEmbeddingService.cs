using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.ML.Tokenizers;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Embeddings;

namespace UniversityHelper.Shared;

#pragma warning disable CS0618 // Type or member is obsolete

public class OnnxTextEmbeddingService : ITextEmbeddingGenerationService, IEmbeddingGenerator<string, Embedding<float>>
{
    private readonly InferenceSession _session;
    private readonly BertTokenizer _tokenizer;
    private readonly int _maxLength;

    public IReadOnlyDictionary<string, object?> Attributes => new Dictionary<string, object?>();

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        return serviceType.IsInstanceOfType(this) ? this : null;
    }

    public async Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(IEnumerable<string> values, EmbeddingGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var embeddings = await GenerateEmbeddingsAsync(values.ToList(), null, cancellationToken);
        var result = new GeneratedEmbeddings<Embedding<float>>();
        foreach (var embedding in embeddings)
        {
            result.Add(new Embedding<float>(embedding));
        }
        return result;
    }

    public OnnxTextEmbeddingService(string modelPath, int maxLength = 256)
    {
        var onnxPath = Path.Combine(modelPath, "model.onnx");
        var vocabPath = Path.Combine(modelPath, "vocab.txt");

        if (!File.Exists(onnxPath))
            throw new FileNotFoundException($"ONNX model not found: {onnxPath}");
        if (!File.Exists(vocabPath))
            throw new FileNotFoundException($"Vocab file not found: {vocabPath}");

        _session = new InferenceSession(onnxPath);
        _tokenizer = BertTokenizer.Create(vocabPath);
        _maxLength = maxLength;
        
        Console.WriteLine($"✅ ONNX модель загружена: {onnxPath}");
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
            var encodings = data.Select(text => _tokenizer.EncodeToIds(text)).ToList();

            // Подготовка тензоров
            var inputIds = new long[batchSize, _maxLength];
            var attentionMask = new long[batchSize, _maxLength];
            var tokenTypeIds = new long[batchSize, _maxLength];

            for (int i = 0; i < batchSize; i++)
            {
                var ids = encodings[i];
                var len = Math.Min(ids.Count, _maxLength);

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

            var inputIdsFlat = new long[batchSize * _maxLength];
            var attentionMaskFlat = new long[batchSize * _maxLength];
            var tokenTypeIdsFlat = new long[batchSize * _maxLength];

            Buffer.BlockCopy(inputIds, 0, inputIdsFlat, 0, inputIds.Length * sizeof(long));
            Buffer.BlockCopy(attentionMask, 0, attentionMaskFlat, 0, attentionMask.Length * sizeof(long));
            Buffer.BlockCopy(tokenTypeIds, 0, tokenTypeIdsFlat, 0, tokenTypeIds.Length * sizeof(long));

            // Создание ONNX тензоров
            var dimensions = new ReadOnlySpan<int>(new[] { batchSize, _maxLength });
            var inputIdsTensor = new DenseTensor<long>(new Memory<long>(inputIdsFlat), dimensions);
            var attentionMaskTensor = new DenseTensor<long>(new Memory<long>(attentionMaskFlat), dimensions);
            var tokenTypeIdsTensor = new DenseTensor<long>(new Memory<long>(tokenTypeIdsFlat), dimensions);

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

            return (IList<ReadOnlyMemory<float>>)embeddings;
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
        var norm = Math.Sqrt(result.Sum(x => (double)x * x));
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
        _session.Dispose();
    }
}
