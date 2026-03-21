namespace WebApp;

using System.Threading;
using System.IO;

public static class AnalyticsEndpoints
{
    private static int _siteVisits;
    private static int _chatTransitions;

    private static readonly string _visitsFilePath = "site_visits.txt";
    private static readonly string _transitionsFilePath = "chat_transitions.txt";

    static AnalyticsEndpoints()
    {
        _siteVisits = ReadCounterFromFile(_visitsFilePath);
        _chatTransitions = ReadCounterFromFile(_transitionsFilePath);
    }

    public static IResult GetSiteVisits()
    {
        Interlocked.Increment(ref _siteVisits);
        WriteCounterToFile(_visitsFilePath, _siteVisits);
        return Results.Ok(_siteVisits);
    }

    public static IResult GetChatTransitions()
    {
        Interlocked.Increment(ref _chatTransitions);
        WriteCounterToFile(_transitionsFilePath, _chatTransitions);
        return Results.Ok(_chatTransitions);
    }

    public static IResult GetStats()
    {
        // Просто возвращаем текущие значения, не инкрементируя
        return Results.Ok(new { SiteVisits = _siteVisits, ChatTransitions = _chatTransitions });
    }

    private static int ReadCounterFromFile(string filePath)
    {
        if (File.Exists(filePath) && int.TryParse(File.ReadAllText(filePath), out int count))
        {
            return count;
        }
        return 0;
    }

    private static void WriteCounterToFile(string filePath, int count)
    {
        File.WriteAllText(filePath, count.ToString());
    }
}
