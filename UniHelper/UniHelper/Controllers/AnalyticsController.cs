using System.Collections.Concurrent;
using Microsoft.AspNetCore.Mvc;

namespace UniHelper.Controllers;

[ApiController]
[Route("api/analytics")]
public class AnalyticsController : ControllerBase
{
    private static int impressions;
    private static int clicks;
    private static int totalQueries;
    private static int notFoundQueries;
    private static int engagedUsers;
    private static readonly ConcurrentDictionary<string, int> sessionMessageCounts = new();

    [HttpPost("impression")]
    public IActionResult Impression()
    {
        Interlocked.Increment(ref impressions);
        return Ok(new
        {
            impressions
        });
    }

    [HttpPost("click")]
    public IActionResult Click()
    {
        Interlocked.Increment(ref clicks);
        return Ok(new
        {
            clicks
        });
    }

    [HttpGet("statistics")]
    public IActionResult Statistics()
    {
        var ctr = impressions == 0 ? 0 : (double)clicks / impressions;
        var notFoundRate = totalQueries == 0 ? 0 : (double)notFoundQueries / totalQueries;
        
        return Ok(new
        {
            impressions,
            clicks,
            ctr = Math.Round(ctr * 100, 2) + "%",
            totalQueries, 
            notFoundQueries,
            notFoundRate = Math.Round(notFoundRate * 100, 2) + "%",
            engagedUsers
        });
    }

    public static void RecordChatInteraction(string sessionId, bool isFound)
    {
        Interlocked.Increment(ref totalQueries);
        if (!isFound)
            Interlocked.Increment(ref notFoundQueries);

        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            var count = sessionMessageCounts.AddOrUpdate(sessionId, 1, (_, current) => current + 1);
            if (count == 3)
                Interlocked.Increment(ref engagedUsers);
        }
    }
}