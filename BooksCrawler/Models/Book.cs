namespace BooksCrawler.Models;

public record Book
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Title { get; set; } = string.Empty;
    public decimal? Price { get; set; }
    public string Url { get; set; } = string.Empty;

    public int? Year { get; set; }
    public string? Publisher { get; set; }

    public List<string> Authors { get; set; } = [];
}