namespace BooksCrawler.Domain;

public record Book
{
    public string Id { get; init; } = Guid.NewGuid().ToString();
    public string Title { get; init; } = string.Empty;
    public decimal? Price { get; init; }
    public string Url { get; init; } = string.Empty;

    public int? Year { get; init; }
    public string? Publisher { get; init; }

    public List<string> Authors { get; init; } = [];
}