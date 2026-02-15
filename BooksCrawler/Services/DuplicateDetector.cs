namespace BooksCrawler.Services;

public class DuplicateDetector
{
    private readonly HashSet<string> _seen = new();
    private int _rejectedCount;

    public bool IsDuplicate(string url)
    {
        var wasAdded = _seen.Add(url);

        if (!wasAdded)
            _rejectedCount++;

        return !wasAdded;
    }

    public int UniqueCount => _seen.Count;
    public int RejectedCount => _rejectedCount;
}