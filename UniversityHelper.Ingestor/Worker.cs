using Microsoft.Extensions.Logging;

namespace UniversityHelper.Ingestor;

public class Worker(
    ScraperService scraperService, 
    IConfiguration configuration, 
    ILogger<Worker> logger, 
    IHostApplicationLifetime hostApplicationLifetime) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var url = configuration["ScrapeUrl"];
        logger.LogInformation("Worker started. Target URL: {Url}", url);

        if (!string.IsNullOrEmpty(url))
        {
            try 
            {
                logger.LogInformation("Initiating scraping...");
                await scraperService.ScrapeAndStoreAsync(url);
                logger.LogInformation("Scraping finished.");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to scrape URL {Url}", url);
            }
        }
        else 
        {
            logger.LogWarning("No ScrapeUrl configured.");
        }
        
        logger.LogInformation("Worker completed execution. Stopping application...");
        hostApplicationLifetime.StopApplication();
    }
}
