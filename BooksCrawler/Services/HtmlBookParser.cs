using HtmlAgilityPack;
using BooksCrawler.Domain;
using System.Globalization;
using System.Text.RegularExpressions;

namespace BooksCrawler.Services;

public class HtmlBookParser
{
    public List<string> ExtractBookLinks(string html, string baseUrl)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var linkNodes = new HtmlNodeCollection(null);

        // Strategia 1: Szukamy linków w tytułach produktów (najczęstszy wzorzec na TaniaKsiazka)
        // Szukamy elementów <h3> które zawierają <a> z href
        var nodes1 = doc.DocumentNode.SelectNodes("//h3/a[@href]");
        if (nodes1 != null) foreach (var n in nodes1) linkNodes.Add(n);

        // Strategia 2: Szukamy linków z klasą "product-title" (czasami używane w widoku listy)
        var nodes2 = doc.DocumentNode.SelectNodes("//a[contains(@class, 'product-title')]");
        if (nodes2 != null) foreach (var n in nodes2) linkNodes.Add(n);

        // Strategia 3: Szukamy linków wewnątrz kontenera produktu (często div z klasą "product-..." lub "offer-...")
        // To jest bardzo szeroki selektor, więc będziemy musieli mocno filtrować wyniki
        var nodes3 = doc.DocumentNode.SelectNodes("//div[contains(@class, 'product')]//a[@href]");
        if (nodes3 != null) foreach (var n in nodes3) linkNodes.Add(n);

        if (linkNodes.Count == 0) return [];

        var links = new List<string>();
        var baseUri = new Uri(baseUrl);

        foreach (var node in linkNodes)
        {
            var href = node.GetAttributeValue("href", "");

            // Filtrowanie (Cleanup)
            if (string.IsNullOrWhiteSpace(href)) continue;

            // Ignorujemy linki do autorów, kategorii, javascript, koszyka itp.
            if (href.Contains("javascript:")) continue;
            if (href.Contains("/autor/")) continue;
            if (href.Contains("/wydawnictwo/")) continue;
            if (href.Contains("/serie/")) continue;
            if (href.Contains("dodaj-do-schowka")) continue;
            if (href.Contains("koszyk")) continue;

            // Link do produktu na TaniaKsiazka zazwyczaj nie ma dziwnych prefiksów, 
            // ale dla pewności sprawdźmy czy nie jest obrazkiem
            if (href.EndsWith(".jpg") || href.EndsWith(".png")) continue;

            // Budowanie pełnego URL
            var absoluteUri = href.StartsWith("http") ? href : new Uri(baseUri, href).ToString();

            // Dodatkowe zabezpieczenie: TaniaKsiazka.pl ma linki w formacie *-p-*.html lub *-p-*.htm 
            // (gdzie 'p' oznacza produkt, a potem jest ID). Np. "tytul-ksiazki-p-12345.html"
            // To najlepszy sposób na odróżnienie produktu od śmieci.
            if (absoluteUri.Contains("-p-"))
            {
                links.Add(absoluteUri);
            }
        }

        return links.Distinct().ToList();
    }
    public Book? ParseBookDetails(string html, string url)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        // Tytuł i Cena 
        var titleNode = doc.DocumentNode.SelectSingleNode("//h1");
        var priceNode = doc.DocumentNode.SelectSingleNode("//meta[@itemprop='price']");

        if (titleNode == null) return null;

        decimal? price = null;
        if (priceNode != null)
        {
            var priceText = priceNode.GetAttributeValue("content", "");
            if (decimal.TryParse(priceText.Replace(",", "."), NumberStyles.Any, CultureInfo.InvariantCulture, out var p))
                price = p;
        }

        // Autorzy: Szukamy linków w sekcji autorów (często pod tytułem)
        var authorNodes = doc.DocumentNode.SelectNodes("//a[contains(@href, '/autor/')]");
        var authors = authorNodes?.Select(n => n.InnerText.Trim()).Distinct().ToList() ?? ["Nieznany Autor"];

        // Wydawnictwo: Szukamy linku do wydawnictwa (często w tabeli szczegółów)
        var publisherNode = doc.DocumentNode.SelectSingleNode("//a[contains(@href, '/wydawnictwo/')]");
        var publisher = publisherNode?.InnerText.Trim();

        // Rok wydania: Szukamy w tabeli szczegółów (często wiersz z "Data wydania:" lub "Rok wydania:")
        // Na TaniaKsiazka.pl to często jest w liście <li>Data wydania: <span>2024</span></li>
        int? year = null;
        var yearNode = doc.DocumentNode.SelectSingleNode("//li[contains(., 'Data wydania')]//span")
                       ?? doc.DocumentNode.SelectSingleNode("//li[contains(., 'Rok wydania')]//span");

        if (yearNode != null)
        {
            // Wyciągamy rok z daty (np. 2024-05-12 -> 2024)
            var yearText = Regex.Match(yearNode.InnerText, @"\d{4}").Value;
            if (int.TryParse(yearText, out var y)) year = y;
        }

        return new Book
        {
            Id = url.GetHashCode().ToString(),
            Title = titleNode.InnerText.Trim(),
            Price = price,
            Url = url,
            Authors = authors,
            Publisher = publisher, // <-- Nowe pole
            Year = year           // <-- Nowe pole
        };
    }

}
