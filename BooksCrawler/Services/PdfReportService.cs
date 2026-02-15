// File: BooksCrawler.Services/PdfReportService.cs
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using iTextSharp.text;
using iTextSharp.text.pdf;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using BooksCrawler.Configuration;
using BooksCrawler.Models;

namespace BooksCrawler.Services;

public sealed class PdfReportService : IDisposable
{
    private readonly ReportOptions _config;
    private readonly ILogger _logger;

    public PdfReportService(IOptions<AppOptions> options, ILogger<PdfReportService> logger)
    {
        _config = options.Value.Report;
        _logger = logger;
    }

    public async Task GenerateReportAsync(
        string filePath,
        string searchQuery,
        int maxPages,
        string seedUrl,
        CrawlStats? crawlStats,
        Dictionary<string, string> stats,
        List<Neo4JService.AnalysisResult> analysis,
        List<Book> topBooks)
    {
        await Task.Run(() =>
        {
            var dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);

            using var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
            using var doc = new Document(PageSize.A4, 50, 50, 50, 50);
            PdfWriter.GetInstance(doc, stream);
            doc.Open();

            // Fonty - obsługa polskich znaków
            string fontPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "arial.ttf");
            if (!File.Exists(fontPath)) fontPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "DejaVuSans.ttf");

            // Fallback font jeśli żaden nie istnieje
            BaseFont bf = File.Exists(fontPath)
                ? BaseFont.CreateFont(fontPath, BaseFont.IDENTITY_H, BaseFont.EMBEDDED)
                : BaseFont.CreateFont(BaseFont.HELVETICA, BaseFont.CP1250, BaseFont.NOT_EMBEDDED);

            var titleFont = new Font(bf, 24, Font.BOLD);
            var sectionFont = new Font(bf, 16, Font.BOLD);
            var normalFont = new Font(bf, 11, Font.NORMAL);
            var boldFont = new Font(bf, 11, Font.BOLD);

            // 1. STRONA TYTUŁOWA
            doc.Add(new Paragraph("Raport Crawlera Książek", titleFont) { Alignment = Element.ALIGN_CENTER, SpacingAfter = 20 });
            doc.Add(new Paragraph($"Data: {DateTime.Now:yyyy-MM-dd HH:mm}", normalFont) { Alignment = Element.ALIGN_CENTER, SpacingAfter = 5 });

            // Autor z konfiguracji
            if (!string.IsNullOrEmpty(_config.AuthorName))
            {
                doc.Add(new Paragraph($"Autor: {_config.AuthorName}", normalFont) { Alignment = Element.ALIGN_CENTER, SpacingAfter = 5 });
            }

            // Parametry crawl
            doc.Add(new Paragraph($"Zapytanie: \"{searchQuery}\"", normalFont) { Alignment = Element.ALIGN_CENTER, SpacingAfter = 5 });
            doc.Add(new Paragraph($"Zakres: {maxPages} stron", normalFont) { Alignment = Element.ALIGN_CENTER, SpacingAfter = 5 });

            // Seed URL przekazany jako parametr
            doc.Add(new Paragraph($"Seed URL: {seedUrl}", normalFont) { Alignment = Element.ALIGN_CENTER, SpacingAfter = 25 });

            // 2. PODSUMOWANIE (Statystyki)
            doc.Add(new Paragraph("Podsumowanie:", sectionFont) { SpacingAfter = 10 });
            var statTable = new PdfPTable(2) { WidthPercentage = 100, HorizontalAlignment = Element.ALIGN_LEFT, SpacingAfter = 20 };
            statTable.SetWidths(new float[] { 40f, 60f });

            // Dopnij brakujący wpis z crawlStats (jeśli nie został dołożony wcześniej)
            if (crawlStats != null && !stats.ContainsKey("Odrzucone (brak autora)"))
            {
                stats["Odrzucone (brak autora)"] = crawlStats.MissingAuthorRejected.ToString(); // <-- CHANGE THIS!
            }

            foreach (var kv in stats)
            {
                statTable.AddCell(new PdfPCell(new Phrase(kv.Key, boldFont)) { BackgroundColor = BaseColor.LightGray, Padding = 5 });
                statTable.AddCell(new PdfPCell(new Phrase(kv.Value, normalFont)) { Padding = 5 });
            }

            doc.Add(statTable);

            // 3. WYNIKI ANALIZY
            doc.Add(new Paragraph("Wyniki Analizy:", sectionFont) { SpacingAfter = 15 });

            foreach (var result in analysis)
            {
                // Nagłówek sekcji z parametrami
                var header = result.QueryName;
                if (!string.IsNullOrEmpty(result.Parameters))
                    header += $" [{result.Parameters}]";

                doc.Add(new Paragraph(header, boldFont) { SpacingBefore = 10, SpacingAfter = 5 });

                // Sprawdź czy to analiza bez tytułów książek (Top Autorzy lub Top Wydawnictwa)
                bool isSimpleTable = result.QueryName.Contains("Autorów") || result.QueryName.Contains("Wydawnictw");

                if (isSimpleTable)
                {
                    // TABELA 3-KOLUMNOWA: #, Nazwa, Wartość
                    var table = new PdfPTable(3) { WidthPercentage = 100, SpacingAfter = 10 };
                    table.SetWidths(new float[] { 8f, 62f, 30f });

                    // Nagłówki
                    table.AddCell(new PdfPCell(new Phrase("#", boldFont)) { BackgroundColor = BaseColor.Gray });
                    table.AddCell(new PdfPCell(new Phrase("Nazwa", boldFont)) { BackgroundColor = BaseColor.Gray });
                    table.AddCell(new PdfPCell(new Phrase("Wartość", boldFont)) { BackgroundColor = BaseColor.Gray });

                    int idx = 1;
                    foreach (var line in result.Results)
                    {
                        var parsed = ParseLine(line);
                        table.AddCell(new PdfPCell(new Phrase(idx++.ToString(), normalFont)));
                        table.AddCell(new PdfPCell(new Phrase(parsed.Author, normalFont))); // Nazwa autora/wydawnictwa
                        table.AddCell(new PdfPCell(new Phrase(parsed.Value, normalFont)));
                    }

                    doc.Add(table);
                }
                else
                {
                    // TABELA 4-KOLUMNOWA: #, Tytuł, Autor, Wartość
                    var table = new PdfPTable(4) { WidthPercentage = 100, SpacingAfter = 10 };
                    table.SetWidths(new float[] { 8f, 40f, 30f, 22f });

                    // Nagłówki
                    table.AddCell(new PdfPCell(new Phrase("#", boldFont)) { BackgroundColor = BaseColor.Gray });
                    table.AddCell(new PdfPCell(new Phrase("Tytuł", boldFont)) { BackgroundColor = BaseColor.Gray });
                    table.AddCell(new PdfPCell(new Phrase("Autor", boldFont)) { BackgroundColor = BaseColor.Gray });
                    table.AddCell(new PdfPCell(new Phrase("Wartość", boldFont)) { BackgroundColor = BaseColor.Gray });

                    int idx = 1;
                    foreach (var line in result.Results)
                    {
                        var parsed = ParseLine(line);
                        table.AddCell(new PdfPCell(new Phrase(idx++.ToString(), normalFont)));
                        table.AddCell(new PdfPCell(new Phrase(parsed.Title, normalFont)));
                        table.AddCell(new PdfPCell(new Phrase(parsed.Author, normalFont)));
                        table.AddCell(new PdfPCell(new Phrase(parsed.Value, normalFont)));
                    }

                    doc.Add(table);
                }
            }

            doc.Close();
        });

        _logger.LogInformation("PDF wygenerowany.");
    }

    // ZAKTUALIZOWANY PARSER (Zgodny z ViewModel)
    private (string Title, string Author, string Value) ParseLine(string line)
    {
        // 1. Format Pełny: "Tytuł - Autor (Wartość)"
        // Obsługuje teraz dowolny tekst w nawiasie, np. "120 PLN", "Rok 2024"
        var m1 = Regex.Match(line, @"^(?<title>.+?)\s*-\s*(?<author>.+?)\s*\((?<value>.+?)\)$");
        if (m1.Success)
        {
            return (m1.Groups["title"].Value.Trim(),
                    m1.Groups["author"].Value.Trim(),
                    m1.Groups["value"].Value.Trim());
        }

        // 2. Format Prosty: "Nazwa (Wartość)"
        // Np. "Wydawnictwo X (50 książek)" lub "Jan Kowalski (10 książek)"
        var m2 = Regex.Match(line, @"^(?<author>.+?)\s+\((?<value>.+?)\)$");
        if (m2.Success)
        {
            return ("—", // Tytuł pusty dla wydawców/autorów
                    m2.Groups["author"].Value.Trim(),
                    m2.Groups["value"].Value.Trim());
        }

        // Fallback
        return ("—", "—", line);
    }

    public void Dispose() { }
}
