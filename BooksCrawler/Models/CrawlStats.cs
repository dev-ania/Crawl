namespace BooksCrawler.Models;

public class CrawlStats
{
    public int PagesProcessed { get; set; }
    public int TotalFound { get; set; }
    public int UniqueAdded { get; set; }
    public int DuplicatesRejected { get; set; }
    public int MissingAuthorRejected { get; set; }
}