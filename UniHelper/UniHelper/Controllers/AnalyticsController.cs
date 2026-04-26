using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using UniHelper.DTOs;

namespace UniHelper.Controllers;

[ApiController]
[Route("api/analytics")]
public class AnalyticsController : ControllerBase
{
    private const string FilePath = "analytics.json";
    private static readonly object _fileLock = new();
    private static readonly ConcurrentDictionary<string, int> sessionMessageCounts = new();

    private static AnalyticsData Load()
    {
        lock (_fileLock)
        {
            if (!System.IO.File.Exists(FilePath)) return new AnalyticsData();
            try {
                var json = System.IO.File.ReadAllText(FilePath);
                return JsonSerializer.Deserialize<AnalyticsData>(json) ?? new AnalyticsData();
            } catch { return new AnalyticsData(); }
        }
    }

    private static void Save(AnalyticsData data)
    {
        lock (_fileLock)
        {
            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
            System.IO.File.WriteAllText(FilePath, json);
        }
    }

    [HttpPost("impression")]
    public IActionResult Impression()
    {
        var data = Load();
        data.Impressions++;
        Save(data);
        return Ok(new { data.Impressions });
    }

    [HttpPost("click")]
    public IActionResult Click()
    {
        var data = Load();
        data.Clicks++;
        Save(data);
        return Ok(new { data.Clicks });
    }

    [HttpPost("like")]
    public IActionResult Like()
    {
        var data = Load();
        data.Likes++;
        Save(data);
        return Ok(new { data.Likes });
    }

    [HttpPost("dislike")]
    public IActionResult Dislike()
    {
        var data = Load();
        data.Dislikes++;
        Save(data);
        return Ok(new { data.Dislikes });
    }

    [HttpPost("track-user")]
    public IActionResult TrackUser()
    {
        var data = Load();
        data.Users++;
        Save(data);
        return Ok(new { data.Users });
    }

    [HttpGet("statistics")]
    public IActionResult Statistics()
    {
        var data = Load();
        var ctr = data.Impressions == 0 ? 0 : (double)data.Clicks / data.Impressions;
        var notFoundRate = data.TotalQueries == 0 ? 0 : (double)data.NotFoundQueries / data.TotalQueries;
        
        return Ok(new
        {
            data.Impressions,
            data.Clicks,
            ctr = Math.Round(ctr * 100, 2) + "%",
            data.TotalQueries, 
            data.NotFoundQueries,
            notFoundRate = Math.Round(notFoundRate * 100, 2) + "%",
            data.EngagedUsers,
            data.Users,
            data.Likes,
            data.Dislikes
        });
    }

    public static void RecordChatInteraction(string sessionId, bool isFound)
    {
        var data = Load();
        data.TotalQueries++;
        if (!isFound) data.NotFoundQueries++;

        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            var count = sessionMessageCounts.AddOrUpdate(sessionId, 1, (_, current) => current + 1);
            if (count == 3) data.EngagedUsers++;
        }
        Save(data);
    }
}