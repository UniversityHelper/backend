using Microsoft.AspNetCore.Mvc;

namespace UniHelper.Controllers;

[ApiController]
[Route("api/analytics")]
public class AnalyticsController : ControllerBase
{
    private static int impressions;
    private static int clicks;

    [HttpPost("impression")]
    public IActionResult Impression()
    {
        impressions++;
        return Ok(new { impressions });
    }

    [HttpPost("click")]
    public IActionResult Click()
    {
        clicks++;
        return Ok(new { clicks });
    }

    [HttpGet("statistics")]
    public IActionResult Statistics()
    {
        var ctr = impressions == 0 ? 0 : (double)clicks / impressions;
        return Ok(new
        {
            impressions,
            clicks,
            ctr
        });
    }
}