namespace BooksCrawler.Services;

public class DuplicateDetector
{
    // Używamy ConcurrentDictionary jeśli planujemy wielowątkowość w przyszłości
    private readonly HashSet<string> _seen = [];
    public bool IsDuplicate(string url) => !_seen.Add(url);
}