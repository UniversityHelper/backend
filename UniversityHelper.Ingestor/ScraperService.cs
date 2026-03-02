#pragma warning disable SKEXP0050
#pragma warning disable SKEXP0001
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using Microsoft.SemanticKernel.Memory;
using Microsoft.SemanticKernel.Text;
using UniversityHelper.Shared;
using UglyToad.PdfPig;
using System.Drawing;
using System.Drawing.Imaging;
using System.Text;
using Docnet.Core;
using Docnet.Core.Models;
using Tesseract;

namespace UniversityHelper.Ingestor;

public class ScraperService(ISemanticTextMemory memory, ILogger<ScraperService> logger, IConfiguration configuration)
{
    public async Task ScrapeAndStoreAsync(string url)
    {
        logger.LogInformation("Starting scraping for URL: {Url}", url);
        
        try 
        {
            string text;
            
            // Определяем, является ли URL ссылкой на PDF
            if (url.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            {
                logger.LogInformation("Detected PDF document. Using PDF parser...");
                text = await ParsePdfAsync(url);
            }
            else
            {
                logger.LogInformation("Detected HTML page. Using Playwright...");
                text = await ParseHtmlAsync(url);
            }

            logger.LogInformation("Text extracted. Length: {Length} characters", text.Length);

            if (string.IsNullOrWhiteSpace(text))
            {
                logger.LogWarning("No text extracted from {Url}. Skipping.", url);
                return;
            }

            logger.LogInformation("Splitting text into chunks...");
            var chunks = SplitWithSlidingWindow(text, chunkSize: 800, overlapSize: 200);
            logger.LogInformation("Created {Count} chunks", chunks.Count);
                
            for (var i = 0; i < chunks.Count; i++)
            {
                var chunk = chunks[i];
                logger.LogInformation("Saving chunk {Index}/{Total} to memory...", i + 1, chunks.Count);
                await memory.SaveInformationAsync("university-info", chunk, Guid.NewGuid().ToString());
            }
            
            logger.LogInformation("Scraping completed successfully.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error occurred during scraping.");
            throw;
        }
    }

    private async Task<string> ParseHtmlAsync(string url)
    {
        logger.LogInformation("Initializing Playwright...");
        using var playwright = await Playwright.CreateAsync();
        
        logger.LogInformation("Launching Chromium...");
        await using var browser = await playwright.Chromium.LaunchAsync();
        
        logger.LogInformation("Creating new page...");
        var page = await browser.NewPageAsync();
        
        logger.LogInformation("Navigating to {Url}...", url);
        await page.GotoAsync(url);

        logger.LogInformation("Extracting text content...");
        var text = await page.InnerTextAsync("body");
        
        return text;
    }

    private async Task<string> ParsePdfAsync(string url)
    {
        logger.LogInformation("Downloading PDF from {Url}...", url);
        
        using var httpClient = new HttpClient();
        var pdfBytes = await httpClient.GetByteArrayAsync(url);
        
        logger.LogInformation("PDF downloaded. Size: {Size} bytes", pdfBytes.Length);
        logger.LogInformation("Parsing PDF content with PdfPig...");
        
        var textBuilder = new StringBuilder();
        
        using (var pdfDocument = PdfDocument.Open(pdfBytes))
        {
            logger.LogInformation("PDF has {PageCount} pages", pdfDocument.NumberOfPages);
            
            for (int i = 1; i <= pdfDocument.NumberOfPages; i++)
            {
                var page = pdfDocument.GetPage(i);
                var pageText = page.Text;
                
                if (!string.IsNullOrWhiteSpace(pageText))
                {
                    textBuilder.AppendLine(pageText);
                    logger.LogInformation("Extracted text from page {PageNumber}/{TotalPages} ({Length} chars)", 
                        i, pdfDocument.NumberOfPages, pageText.Length);
                }
            }
        }
        
        var extractedText = textBuilder.ToString();
        
        // Если текст не был извлечён (скорее всего скан) — используем OCR
        if (string.IsNullOrWhiteSpace(extractedText))
        {
            logger.LogInformation("No text found via PdfPig (likely scanned PDF). Falling back to OCR...");
            extractedText = await OcrPdfAsync(pdfBytes);
        }
        
        return extractedText;
    }

    private Task<string> OcrPdfAsync(byte[] pdfBytes)
    {
        return Task.Run(() =>
        {
            var tessdataPath = configuration["AiSettings:TessdataPath"] 
                ?? Path.Combine(AppContext.BaseDirectory, "tessdata");
            
            logger.LogInformation("Using tessdata from: {TessdataPath}", tessdataPath);
            
            var textBuilder = new StringBuilder();
            
            using var docReader = DocLib.Instance.GetDocReader(
                pdfBytes,
                new PageDimensions(2480, 3508)); // A4 at 300 DPI

            var pageCount = docReader.GetPageCount();
            logger.LogInformation("OCR: PDF has {PageCount} pages", pageCount);

            using var engine = new TesseractEngine(tessdataPath, "rus+eng", EngineMode.Default);

            for (int i = 0; i < pageCount; i++)
            {
                logger.LogInformation("OCR: Processing page {Page}/{Total}...", i + 1, pageCount);
                
                using var pageReader = docReader.GetPageReader(i);
                var width = pageReader.GetPageWidth();
                var height = pageReader.GetPageHeight();
                var rawBytes = pageReader.GetImage(); // BGRA byte array

                // Конвертируем BGRA в Bitmap
                using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
                var bitmapData = bitmap.LockBits(
                    new Rectangle(0, 0, width, height),
                    ImageLockMode.WriteOnly,
                    PixelFormat.Format32bppArgb);

                System.Runtime.InteropServices.Marshal.Copy(rawBytes, 0, bitmapData.Scan0, rawBytes.Length);
                bitmap.UnlockBits(bitmapData);

                // Конвертируем в PNG для Tesseract
                using var ms = new System.IO.MemoryStream();
                bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                ms.Position = 0;

                using var pix = Pix.LoadFromMemory(ms.ToArray());
                using var tesseractPage = engine.Process(pix);
                var pageText = tesseractPage.GetText();
                
                if (!string.IsNullOrWhiteSpace(pageText))
                {
                    textBuilder.AppendLine(pageText);
                    logger.LogInformation("OCR page {Page}: extracted {Length} chars", i + 1, pageText.Length);
                }
            }
            
            return textBuilder.ToString();
        });
    }

    /// <summary>
    /// Разбивает текст на чанки со скользящим окном (overlap).
    /// Каждый чанк содержит часть предыдущего, чтобы контекст не терялся на границах.
    /// Это особенно важно для коротких строк (названий направлений),
    /// которые иначе попадают в изолированные чанки без контекста.
    /// </summary>
    private static List<string> SplitWithSlidingWindow(string text, int chunkSize, int overlapSize)
    {
        var chunks = new List<string>();
        // Нормализуем переносы строк
        var normalized = text.Replace("\r\n", "\n").Replace("\r", "\n");
        var words = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        int start = 0;
        while (start < words.Length)
        {
            // Собираем чанк до chunkSize символов
            var sb = new StringBuilder();
            int end = start;
            while (end < words.Length && sb.Length + words[end].Length + 1 <= chunkSize)
            {
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(words[end]);
                end++;
            }
            // Если не добавили ни одного слова — добавляем принудительно, чтобы не зависнуть
            if (end == start) end = start + 1;

            var chunk = sb.ToString().Trim();
            if (!string.IsNullOrWhiteSpace(chunk))
                chunks.Add(chunk);

            // Откатываемся на overlapSize символов назад для следующего чанка
            if (end >= words.Length) break;

            // Находим точку начала следующего чанка с учётом overlap
            int overlapChars = 0;
            int next = end;
            while (next > start + 1 && overlapChars < overlapSize)
            {
                next--;
                overlapChars += words[next].Length + 1;
            }
            start = next;
        }

        return chunks;
    }
}
