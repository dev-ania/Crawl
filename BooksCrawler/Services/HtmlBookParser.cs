using HtmlAgilityPack;
using System.Globalization;
using System.Text.RegularExpressions;
using BooksCrawler.Models;
using Microsoft.Extensions.Logging;

namespace BooksCrawler.Services;

public class HtmlBookParser
{
    private readonly ILogger<HtmlBookParser> _logger;

    public HtmlBookParser(ILogger<HtmlBookParser> logger)
    {
        _logger = logger;
    }

    public List<Book> ParseBooksFromList(string html, string baseUrl)
    {
        var books = new List<Book>();
        var seenUrls = new HashSet<string>();
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var nodes = doc.DocumentNode.SelectNodes("//a[contains(@class, 'ecommerce-datalayer')]");

        if (nodes != null)
        {
            foreach (var node in nodes)
            {
                var href = node.GetAttributeValue("href", "");
                if (string.IsNullOrWhiteSpace(href)) continue;

                if (!href.StartsWith("http"))
                    href = baseUrl.TrimEnd('/') + "/" + href.TrimStart('/');

                if (seenUrls.Contains(href))
                    continue;

                seenUrls.Add(href);

                var title = node.GetAttributeValue("data-name", "").Trim();
                if (string.IsNullOrEmpty(title))
                    title = node.InnerText.Trim();

                decimal? price = null;
                var priceText = node.GetAttributeValue("data-price", "").Replace(",", ".");
                if (decimal.TryParse(priceText, NumberStyles.Any, CultureInfo.InvariantCulture, out var p))
                    price = p;

                // data-brand to wydawnictwo, nie autor
                var publisher = node.GetAttributeValue("data-brand", "").Trim();

                books.Add(new Book
                {
                    Id = Guid.NewGuid().ToString(),
                    Title = title,
                    Price = price,
                    Url = href,
                    // ZMIANA: Pusta lista zamiast "Nieznany Autor". 
                    // Dzięki temu łatwiej odfiltrować braki w serwisie.
                    Authors = new List<string>(),
                    Publisher = !string.IsNullOrEmpty(publisher) ? publisher : null,
                    Year = null
                });
            }
        }
        return books;
    }

    public void EnrichBookDetails(Book book, string html)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var authorsList = new List<string>();
        _logger.LogInformation("=== Przetwarzanie szczegółów dla: {Title} ===", book.Title);

        // Metoda 1: Extract from div.product-info-author
        var authorDiv = doc.DocumentNode.SelectSingleNode("//div[contains(@class, 'product-info-author')]");
        if (authorDiv != null)
        {
            var authorText = authorDiv.InnerText.Trim();
            authorText = System.Net.WebUtility.HtmlDecode(authorText);
            authorText = Regex.Replace(authorText, @"\s+", " ");
            authorText = Regex.Replace(authorText, @"^(autor|author)\s*:\s*", "", RegexOptions.IgnoreCase).Trim();

            var separators = new[] { ',', ';' };
            var authors = authorText.Split(separators, StringSplitOptions.RemoveEmptyEntries)
                                    .Select(a => a.Trim())
                                    .Where(a => !string.IsNullOrEmpty(a) && a.Length > 1)
                                    .ToList();

            if (authors.Any())
            {
                authorsList.AddRange(authors);
            }
        }

        // Metoda 2: Linki /autor/ (Fallback)
        if (!authorsList.Any())
        {
            var authorLinks = doc.DocumentNode.SelectNodes("//a[contains(@href, '/autor/')]");
            if (authorLinks != null)
            {
                foreach (var link in authorLinks)
                {
                    var authorName = link.InnerText.Trim();
                    authorName = System.Net.WebUtility.HtmlDecode(authorName);
                    authorName = Regex.Replace(authorName, @"\s+", " ");

                    if (!string.IsNullOrEmpty(authorName) && !authorsList.Contains(authorName))
                    {
                        authorsList.Add(authorName);
                    }
                }
            }
        }

        // Aktualizacja obiektu książki
        if (authorsList.Any())
        {
            book.Authors = authorsList;
            _logger.LogInformation("✓ Zaktualizowano autorów: {Authors}", string.Join(", ", authorsList));
        }
        else
        {
            _logger.LogWarning("✗ Nie znaleziono autorów dla: {Title}", book.Title);
            // Nie ustawiamy "Nieznany Autor" - zostawiamy pustą listę, by crawler ją odrzucił
        }

        // Wydawnictwo i Rok (reszta bez zmian)
        var rows = doc.DocumentNode.SelectNodes("//tr");
        if (rows != null)
        {
            foreach (var row in rows)
            {
                var cells = row.SelectNodes("th|td");
                if (cells != null && cells.Count >= 2)
                {
                    var label = cells[0].InnerText.Trim().ToLower();
                    var value = cells[1].InnerText.Trim();

                    if (label.Contains("wydawnictwo"))
                    {
                        if (string.IsNullOrEmpty(book.Publisher)) book.Publisher = value;
                    }
                    else if (label.Contains("rok wydania") || label.Contains("data wydania"))
                    {
                        var match = Regex.Match(value, @"\d{4}");
                        if (match.Success && int.TryParse(match.Value, out var y)) book.Year = y;
                    }
                }
            }
        }

        if (string.IsNullOrEmpty(book.Publisher))
        {
            var pubLink = doc.DocumentNode.SelectSingleNode("//a[contains(@href, '/wydawnictwo/')]");
            if (pubLink != null) book.Publisher = pubLink.InnerText.Trim();
        }
    }

    public string? ParseNextPageLink(string html, string baseUrl)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        var nextNode = doc.DocumentNode.SelectSingleNode("//li[contains(@class, 'next')]/a")
                       ?? doc.DocumentNode.SelectSingleNode("//a[contains(@class, 'next')]");

        if (nextNode != null)
        {
            var href = nextNode.GetAttributeValue("href", "");
            if (!string.IsNullOrWhiteSpace(href) && !href.Contains("javascript"))
            {
                if (!href.StartsWith("http"))
                    href = baseUrl.TrimEnd('/') + "/" + href.TrimStart('/');
                return href;
            }
        }
        return null;
    }
}
