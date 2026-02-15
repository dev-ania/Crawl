// File: BooksCrawler.ViewModels/MainViewModel.cs
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using BooksCrawler.Configuration;
using BooksCrawler.Models;
using BooksCrawler.Services;
using System.Collections.ObjectModel;
using System.Text.RegularExpressions;

namespace BooksCrawler.ViewModels;

public class UiAnalysisItem
{
    public string Index { get; set; } = "";
    public string Title { get; set; } = "";
    public string Author { get; set; } = "";
    public string Value { get; set; } = "";
}

public partial class MainViewModel : ObservableObject
{
    private readonly IServiceProvider _serviceProvider;
    private readonly Neo4JService _neo4j;
    private readonly PdfReportService _pdf;
    private readonly AppOptions _options;
    private readonly ILogger _logger;

    private List<Book> _downloadedBooks = new();
    private CrawlStats? _lastCrawlStats;

    [ObservableProperty] private string _searchQuery = "c sharp";
    [ObservableProperty] private string _logs = "";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private int _maxPages = 3;

    public ObservableCollection<string> AvailableAnalyses { get; } = new()
    {
        "Top Najdroższych",
        "Top Najtańszych",
        "Tańsze niż",
        "Wydane po roku",
        "Autorzy (Średnia Cena)",
        "Top Wydawnictwa",
        "Top Autorzy (Liczba)",
        "Autorzy - Książki po roku"
    };

    [ObservableProperty] private string _selectedAnalysis = "Top Najdroższych";
    [ObservableProperty] private ObservableCollection<UiAnalysisItem> _analysisResults = new();

    [ObservableProperty] private bool _showParameterInput;
    [ObservableProperty] private string _parameterLabel = "Liczba rekordów:";
    [ObservableProperty] private string _parameterHint = "(domyślnie 10)";
    [ObservableProperty] private string _analysisParameter = "10";

    [ObservableProperty] private bool _showTitleColumn = true;

    public MainViewModel(
        IServiceProvider sp,
        Neo4JService neo4j,
        PdfReportService pdf,
        IOptions<AppOptions> opt,
        ILogger<MainViewModel> log)
    {
        _serviceProvider = sp;
        _neo4j = neo4j;
        _pdf = pdf;
        _options = opt.Value;
        _logger = log;

        AppendLog("System gotowy.");

        PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(SelectedAnalysis))
                UpdateParameterVisibility();
        };

        UpdateParameterVisibility();
    }

    private void UpdateParameterVisibility()
    {
        bool isSimpleAnalysis = SelectedAnalysis.Contains("Top Autorzy") ||
                                SelectedAnalysis.Contains("Top Wydawnictwa");
        ShowTitleColumn = !isSimpleAnalysis;

        if (SelectedAnalysis.StartsWith("Top") || SelectedAnalysis.Contains("Wydawnictwa"))
        {
            ShowParameterInput = true;
            ParameterLabel = "Liczba rekordów:";
            ParameterHint = "(np. 10, 20, 50)";
            if (AnalysisParameter == "50" || AnalysisParameter == "2020") AnalysisParameter = "10";
        }
        else if (SelectedAnalysis.Contains("Tańsze niż"))
        {
            ShowParameterInput = true;
            ParameterLabel = "Cena maksymalna:";
            ParameterHint = "(domyślnie 50 PLN)";
            AnalysisParameter = "50";
        }
        else if (SelectedAnalysis.Contains("Wydane po roku") || SelectedAnalysis.Contains("Książki po roku"))
        {
            ShowParameterInput = true;
            ParameterLabel = "Rok minimalny:";
            ParameterHint = "(np. 2020, 2024)";
            AnalysisParameter = "2020";
        }
        else
        {
            ShowParameterInput = false;
        }
    }

    [RelayCommand]
    private async Task StartCrawlAsync()
    {
        if (string.IsNullOrWhiteSpace(SearchQuery)) return;

        IsBusy = true;
        Logs = "";
        _downloadedBooks.Clear();

        AppendLog($"Crawl: {SearchQuery}...");

        try
        {
            var crawler = _serviceProvider.GetRequiredService<BookCrawlerService>();
            var seed = $"https://www.taniaksiazka.pl/szukaj?q={Uri.EscapeDataString(SearchQuery)}";

            var res = await crawler.RunCrawlAsync(seed, MaxPages, AppendLog);
            _downloadedBooks = res.Books;
            _lastCrawlStats = res.Stats;

            AppendLog($"Pobrano: {_downloadedBooks.Count}. Zapisuję do grafu...");
            foreach (var b in _downloadedBooks) await _neo4j.SaveBookAsync(b);
            AppendLog("Zapisano.");
        }
        catch (Exception ex)
        {
            AppendLog($"Błąd: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task RunAnalysisAsync()
    {
        IsBusy = true;
        AnalysisResults.Clear();

        AppendLog($"Analiza: {SelectedAnalysis}...");

        try
        {
            List<string> raw = new();

            int limitParam = int.TryParse(AnalysisParameter, out var limitVal) ? limitVal : 10;
            decimal priceParam = decimal.TryParse(AnalysisParameter, out var priceVal) ? priceVal : 50m;
            int yearParam = int.TryParse(AnalysisParameter, out var yearVal) ? yearVal : 2020;

            if (SelectedAnalysis.Contains("Najdroższych"))
                raw = await _neo4j.GetBooksByPriceQueryAsync("DESC", null, limitParam);
            else if (SelectedAnalysis.Contains("Najtańszych"))
                raw = await _neo4j.GetBooksByPriceQueryAsync("ASC", null, limitParam);
            else if (SelectedAnalysis.Contains("Tańsze niż"))
                raw = await _neo4j.GetBooksByPriceQueryAsync("ASC", priceParam, limitParam);
            else if (SelectedAnalysis == "Wydane po roku")
                raw = await _neo4j.GetNewReleasesAsync(yearParam, limitParam);
            else if (SelectedAnalysis.Contains("Książki po roku"))
                raw = await _neo4j.GetAuthorsByYearAsync(yearParam, limitParam);
            else if (SelectedAnalysis.Contains("Średnia Cena"))
                raw = await _neo4j.GetAuthorsByAvgPriceAsync(limitParam);
            else if (SelectedAnalysis.Contains("Wydawnictwa"))
                raw = await _neo4j.GetTopPublishersAsync(limitParam);
            else if (SelectedAnalysis.Contains("Top Autorzy"))
                raw = await _neo4j.GetTopAuthorsByCountAsync(limitParam);

            int idx = 1;
            foreach (var line in raw)
            {
                var parsed = ParseAnalysisLine(line);
                AnalysisResults.Add(new UiAnalysisItem
                {
                    Index = (idx++).ToString(),
                    Title = parsed.Title,
                    Author = parsed.Author,
                    Value = parsed.Value
                });
            }

            AppendLog($"Znaleziono {raw.Count} wyników.");
        }
        catch (Exception ex)
        {
            AppendLog($"Błąd: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task GenerateReportAsync()
    {
        IsBusy = true;
        Logs += "\n--- PDF ---\n";

        try
        {
            int reportLimit = 10;

            var allAnalyses = await _neo4j.RunAnalysisAsync(reportLimit);
            var stats = await _neo4j.GetStatisticsAsync();

            if (_lastCrawlStats != null)
            {
                var crawlData = new Dictionary<string, string>
                {
                    ["Pobrane rekordy"] = _lastCrawlStats.UniqueAdded.ToString(),
                    ["Odrzucone duplikaty"] = _lastCrawlStats.DuplicatesRejected.ToString(),
                    ["Przetworzone strony"] = _lastCrawlStats.PagesProcessed.ToString(),
                    ["Odrzucone (brak autora)"] = _lastCrawlStats.MissingAuthorRejected.ToString() // <-- CHANGE THIS!
                };

                stats = crawlData.Concat(stats).ToDictionary(x => x.Key, x => x.Value);
            }

            var books = _downloadedBooks.Any()
                ? _downloadedBooks
                : await _neo4j.GetRecentBooksAsync(20);

            var seedUrl = _options.Crawler.SeedUrl;

            await _pdf.GenerateReportAsync(
                _options.Report.OutputPath,
                SearchQuery,
                MaxPages,
                seedUrl,
                _lastCrawlStats,
                stats,
                allAnalyses,
                books);

            AppendLog($"PDF gotowy: {_options.Report.OutputPath}");
        }
        catch (Exception ex)
        {
            AppendLog($"Błąd PDF: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void AppendLog(string msg) => Logs += $"{DateTime.Now:HH:mm:ss}: {msg}\n";

    private (string Title, string Author, string Value) ParseAnalysisLine(string line)
    {
        var m1 = Regex.Match(line, @"^(?<title>.+?)\s*-\s*(?<author>.+?)\s*\((?<value>.+?)\)$");
        if (m1.Success)
            return (m1.Groups["title"].Value.Trim(), m1.Groups["author"].Value.Trim(), m1.Groups["value"].Value.Trim());

        var m2 = Regex.Match(line, @"^(?<author>.+?)\s+\((?<value>.+?)\)$");
        if (m2.Success)
            return ("—", m2.Groups["author"].Value.Trim(), m2.Groups["value"].Value.Trim());

        return ("—", "—", line);
    }
}
