using System.Net.Http;
using BooksCrawler.Configuration;
using BooksCrawler.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text;

namespace BooksCrawler.Services;

public sealed class BookCrawlerService(
    HttpClient httpClient,
    HtmlBookParser parser,
    DuplicateDetector duplicateDetector,
    IOptions<AppOptions> options,
    ILogger<BookCrawlerService> logger)
{
    private readonly CrawlerOptions _config = options.Value.Crawler;

    // Metoda pomocnicza do bezpiecznego pobierania HTML (omija błąd "Invalid character set")
    private async Task<string> DownloadHtmlAsync(string url)
    {
        try
        {
            // Pobieramy jako bajty, a nie string, żeby ominąć walidację nagłówka Content-Type
            var bytes = await httpClient.GetByteArrayAsync(url);

            // Dekodujemy ręcznie jako UTF-8 (dla TaniaKsiazka.pl to działa poprawnie)
            return Encoding.UTF8.GetString(bytes);
        }
        catch (HttpRequestException ex)
        {
            // Logujemy błąd HTTP (np. 404, 500)
            logger.LogWarning("Błąd HTTP przy pobieraniu {Url}: {Message}", url, ex.Message);
            throw;
        }
    }

    public async Task<List<Book>> RunCrawlAsync(string seedUrl, int maxPages, Action<string> logCallback)
    {
        List<Book> books = [];

        // Nagłówki - udajemy zwykłą przeglądarkę
        httpClient.DefaultRequestHeaders.Clear();
        httpClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36");
        httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,*/*;q=0.8");
        httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "pl-PL,pl;q=0.9,en-US;q=0.8,en;q=0.7");

        for (int page = 1; page <= maxPages; page++)
        {
            // Logika paginacji dla TaniaKsiazka.pl:
            // Strona 1: .../szukaj?q=fraza
            // Strona 2: .../szukaj?q=fraza&page=2

            string currentUrl;
            if (page == 1)
            {
                currentUrl = seedUrl;
            }
            else
            {
                // Jeśli URL ma już parametry (?), dodajemy &page=, w przeciwnym razie ?page=
                var separator = seedUrl.Contains("?") ? "&page=" : "?page=";
                currentUrl = $"{seedUrl}{separator}{page}";
            }

            logCallback($"Pobieranie strony {page}: {currentUrl}");

            try
            {
                // Używamy bezpiecznej metody pobierania
                var listHtml = await DownloadHtmlAsync(currentUrl);

                // Wyciągamy linki
                var links = parser.ExtractBookLinks(listHtml, currentUrl);

                if (links.Count == 0)
                {
                    logCallback("  (Brak wyników na tej stronie)");
                    // Jeśli brak wyników na pierwszej stronie, to nie ma sensu iść dalej
                    if (page == 1) break;
                    else continue;
                }

                logCallback($"  Znaleziono {links.Count} linków.");

                foreach (var link in links)
                {
                    // Pomijamy duplikaty
                    if (duplicateDetector.IsDuplicate(link)) continue;

                    // Opóźnienie (Rate Limiting)
                    await Task.Delay(_config.RequestDelayMs);

                    try
                    {
                        // Pobieramy szczegóły książki
                        var bookHtml = await DownloadHtmlAsync(link);
                        var book = parser.ParseBookDetails(bookHtml, link);

                        if (book != null)
                        {
                            books.Add(book);
                            logCallback($"  -> OK: {book.Title} ({book.Price} PLN)");
                        }
                        else
                        {
                            logCallback($"  ! Nie udało się sparsować: {link}");
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Błąd szczegółów: {Url}", link);
                        logCallback($"  ! Błąd przy książce: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Błąd listy stron: {Url}", currentUrl);
                logCallback($"  ! Błąd pobierania strony {page}: {ex.Message}");
                // Jeśli padła lista, przerywamy pętlę
                break;
            }

            // Opóźnienie między stronami listy
            await Task.Delay(_config.RequestDelayMs);
        }

        return books;
    }
}
