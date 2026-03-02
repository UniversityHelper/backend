﻿using System;
using System.IO;
using System.Threading.Tasks;
using UniversityHelper.Shared;

namespace ConsoleApp16;

class Program
{
    static async Task Main(string[] args)
    {
        var logFile = Path.Combine(AppContext.BaseDirectory, "program_log.txt");
        // Also try writing to project root if possible to make it easier to find
        // But AppContext.BaseDirectory is safer for write permissions.
        
        void Log(string message) 
        {
            try 
            {
                File.AppendAllText(logFile, message + Environment.NewLine);
                Console.WriteLine(message);
            }
            catch {}
        }

        File.WriteAllText(logFile, "Starting...\n");
        Log($"Current directory: {Directory.GetCurrentDirectory()}");
        
        var modelPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../onnx-all-MiniLM-L6-v2"));
        Log($"Model path: {modelPath}");

        if (!Directory.Exists(modelPath))
        {
            Log("Model directory not found! trying relative path");
            modelPath = Path.GetFullPath("onnx-all-MiniLM-L6-v2");
            Log($"Alternative path: {modelPath}");
             if (!Directory.Exists(modelPath))
             {
                 Log("Model directory still not found. Exiting.");
                 return;
             }
        }

        try
        {
            using var service = new OnnxTextEmbeddingService(modelPath);
            var text = "Hello world testing embedding";
            Log($"Generating embedding for: '{text}'");
            
            var result = await service.GenerateEmbeddingsAsync(new[] { text });
            
            Log($"Success! Generated {result.Count} embeddings.");
            if (result.Count > 0)
            {
                var memory = result[0];
                Log($"Embedding length: {memory.Length}");
                var span = memory.Span;
                Log($"First 5 values: {string.Join(", ", span.Slice(0, Math.Min(5, memory.Length)).ToArray())}");
            }
        }
        catch (Exception ex)
        {
            Log($"Error: {ex.Message}");
            Log(ex.StackTrace ?? "");
        }
    }
}