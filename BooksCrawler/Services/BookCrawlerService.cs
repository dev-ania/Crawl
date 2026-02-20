// File: BooksCrawler.Services/BookCrawlerService.cs

using System.Net.Http;
using System.Text;
using Microsoft.Extensions.Logging;
using BooksCrawler.Models;

namespace BooksCrawler.Services;

public class BookCrawlerService
{
    private const string BaseUrl = "https://www.taniaksiazka.pl";

    private readonly HttpClient _httpClient;
    private readonly HtmlBookParser _parser;
    private readonly DuplicateDetector _duplicateDetector;
    private readonly ILogger<BookCrawlerService> _logger;

    public BookCrawlerService(
        HttpClient httpClient,
        HtmlBookParser parser,
        DuplicateDetector duplicateDetector,
        ILogger<BookCrawlerService> logger)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        _httpClient = httpClient;
        _parser = parser;
        _duplicateDetector = duplicateDetector;
        _logger = logger;

        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
    }

    // Zwraca Tuple (List<Book>, CrawlStats)
    public async Task<(List<Book> Books, CrawlStats Stats)> RunCrawlAsync(
        string seedUrl,
        int maxPages,
        Action<string>? logCallback)
    {
        var books = new List<Book>();
        var queue = new Queue<string>();
        queue.Enqueue(seedUrl);

        var stats = new CrawlStats();
        int pageCount = 0;

        while (queue.Count > 0 && pageCount < maxPages)
        {
            var currentUrl = queue.Dequeue();
            pageCount++;
            stats.PagesProcessed++;

            logCallback?.Invoke($"Przetwarzanie strony listy {pageCount}/{maxPages}: {currentUrl}");

            try
            {
                var html = await _httpClient.GetStringAsync(currentUrl);

                // 1) POZIOM LISTY
                var booksFromList = _parser.ParseBooksFromList(html, BaseUrl);
                stats.TotalFound += booksFromList.Count;

                logCallback?.Invoke($"Znaleziono {booksFromList.Count} wstępnych wyników.");

                // 2) POZIOM SZCZEGÓŁÓW
                foreach (var book in booksFromList)
                {
                    if (_duplicateDetector.IsDuplicate(book.Url))
                    {
                        stats.DuplicatesRejected++;
                        logCallback?.Invoke($" Pominięto duplikat: {book.Title}");
                        continue;
                    }

                    try
                    {
                        var detailHtml = await _httpClient.GetStringAsync(book.Url);
                        _parser.EnrichBookDetails(book, detailHtml);
                        await Task.Delay(300);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex,
                            "Błąd pobierania szczegółów dla {Url}. Zachowano dane z listy.",
                            book.Url);
                    }

                    bool hasInvalidAuthor =
                        book.Authors == null || book.Authors.Count == 0 ||
                        book.Authors.Any(a =>
                            string.IsNullOrWhiteSpace(a) ||
                            a.Contains("Nieznany", StringComparison.OrdinalIgnoreCase) ||
                            a.Contains("Unknown", StringComparison.OrdinalIgnoreCase));

                    if (hasInvalidAuthor)
                    {
                        stats.MissingAuthorRejected++;
                        logCallback?.Invoke($" Pominięto pozycję (brak autora): {book.Title}");
                        _logger.LogInformation(
                            "Odrzucono: '{Title}' - brak autora lub autor nieznany - pozycja nieksiążkowa.",
                            book.Title);
                        continue;
                    }

                    books.Add(book);
                    stats.UniqueAdded++;
                }

                // 3) PAGINACJA
                if (pageCount < maxPages)
                {
                    var nextPageLink = _parser.ParseNextPageLink(html, BaseUrl);
                    if (!string.IsNullOrEmpty(nextPageLink) &&
                        !currentUrl.Equals(nextPageLink, StringComparison.OrdinalIgnoreCase))
                    {
                        queue.Enqueue(nextPageLink);
                        logCallback?.Invoke("Dodano kolejną stronę wyników do kolejki.");
                    }
                }
            }
            catch (Exception ex)
            {
                logCallback?.Invoke($"Błąd pobierania strony listy: {ex.Message}");
                _logger.LogError(ex, "Błąd crawlowania strony listy: {Url}", currentUrl);
            }

            await Task.Delay(1000);
        }

        logCallback?.Invoke(
            $"Podsumowanie: znaleziono {stats.TotalFound} pozycji, dodano: {stats.UniqueAdded}, " +
            $"odrzucono duplikaty: {stats.DuplicatesRejected}, odrzucono (brak autora): {stats.MissingAuthorRejected}");

        return (books, stats);
    }
}
