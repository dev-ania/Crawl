using System.IO;
using iTextSharp.text;
using iTextSharp.text.pdf;
using Microsoft.Extensions.Logging;

namespace BooksCrawler.Services;

public sealed class PdfReportService(ILogger<PdfReportService> logger)
{
    public async Task GenerateReportAsync(string filePath, string seedUrl, int count, int dups)
    {
        await Task.Run(() =>
        {
            using var stream = new FileStream(filePath, FileMode.Create);
            var doc = new Document(PageSize.A4);
            PdfWriter.GetInstance(doc, stream);
            doc.Open();

            doc.Add(new Paragraph($"Raport Crawlera (.NET 10)") { Font = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 16) });
            doc.Add(new Paragraph($"Data: {DateTime.Now}"));
            doc.Add(new Paragraph($"Źródło: {seedUrl}"));
            doc.Add(new Paragraph(" "));
            doc.Add(new Paragraph($"Pobrano: {count}"));
            doc.Add(new Paragraph($"Duplikaty: {dups}"));

            doc.Close();
        });

        logger.LogInformation("PDF wygenerowany: {Path}", filePath);
    }
}