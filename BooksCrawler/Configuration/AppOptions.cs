namespace BooksCrawler.Configuration;

public sealed class AppOptions
{
    public Neo4JOptions Neo4J { get; set; } = new();
    public CrawlerOptions Crawler { get; set; } = new();
    public ReportOptions Report { get; set; } = new();
}

public sealed class Neo4JOptions
{
    public string Uri { get; set; } = string.Empty;
    public string User { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public sealed class CrawlerOptions
{
    public string SeedUrl { get; set; } = string.Empty;
    public int MaxPages { get; set; } = 2;
    public int RequestDelayMs { get; set; } = 1000;
}

public sealed class ReportOptions
{
    public string OutputPath { get; set; } = "report.pdf";
}