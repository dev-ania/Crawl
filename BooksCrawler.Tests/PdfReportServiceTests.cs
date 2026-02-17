using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using BooksCrawler.Configuration; // <-- DODANE: dla AppOptions
using BooksCrawler.Models;
using BooksCrawler.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;

namespace BooksCrawler.Tests;

[TestFixture]
public class PdfReportServiceTests
{
    [Test]
    public async Task GenerateReportAsync_CreatesPdfFile_AndAddsMissingAuthorStatWhenAbsent()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"BooksCrawler_{Guid.NewGuid():N}.pdf");

        try
        {
            var appOptions = new AppOptions
            {
                Report = new ReportOptions { AuthorName = "Tester" }
            };
            var opts = Options.Create(appOptions);

            var logger = new Mock<ILogger<PdfReportService>>();
            var sut = new PdfReportService(opts, logger.Object);

            var crawlStats = new CrawlStats { MissingAuthorRejected = 7 };
            var stats = new Dictionary<string, string>
            {
                ["Liczba książek w bazie"] = "1"
                // celowo bez "Odrzucone (brak autora)"
            };

            var analysis = new List<Neo4JService.AnalysisResult>
            {
                new("Top 2 Najdroższych", new List<string>
                {
                    "Tytuł - Autor (120 PLN)",
                    "Wydawnictwo X (50 książek)"
                }, "Limit: 2")
            };

            await sut.GenerateReportAsync(
                filePath: tmp,
                searchQuery: "c sharp",
                maxPages: 1,
                seedUrl: "https://seed",
                crawlStats: crawlStats,
                stats: stats,
                analysis: analysis,
                topBooks: new List<Book>());

            Assert.That(File.Exists(tmp), Is.True);
            Assert.That(new FileInfo(tmp).Length, Is.GreaterThan(0));
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }
}
