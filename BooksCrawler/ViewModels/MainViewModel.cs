using BooksCrawler.Configuration;
using BooksCrawler.Services;
using Microsoft.Extensions.Options;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Net;

namespace BooksCrawler.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
    private readonly BookCrawlerService _crawler;
    private readonly Neo4JService _neo4j;
    private readonly PdfReportService _pdf;
    private readonly AppOptions _options;

    private string _seedUrl; // To jest Base URL z UI (np. https://www.taniaksiazka.pl)
    private string _searchQuery;
    private int _maxPages;
    private string _logs;
    private bool _isBusy;

    public MainViewModel(
        BookCrawlerService crawler,
        Neo4JService neo4j,
        PdfReportService pdf,
        IOptions<AppOptions> options)
    {
        _crawler = crawler;
        _neo4j = neo4j;
        _pdf = pdf;
        _options = options.Value;

        _seedUrl = "https://www.taniaksiazka.pl";
        _searchQuery = "AI";
        _maxPages = _options.Crawler.MaxPages;
        _logs = "Gotowy (v1.3 - TaniaKsiazka)\n";
        _isBusy = false;

        StartCommand = new RelayCommand(async _ => await RunAsync(), _ => !IsBusy);
    }

    public string SeedUrl
    {
        get => _seedUrl;
        set { _seedUrl = value; OnPropertyChanged(); }
    }

    public string SearchQuery
    {
        get => _searchQuery;
        set { _searchQuery = value; OnPropertyChanged(); }
    }

    public int MaxPages
    {
        get => _maxPages;
        set { _maxPages = value; OnPropertyChanged(); }
    }

    public string Logs
    {
        get => _logs;
        set { _logs = value; OnPropertyChanged(); }
    }

    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            _isBusy = value;
            OnPropertyChanged();
            ((RelayCommand)StartCommand).RaiseCanExecuteChanged();
        }
    }

    public ICommand StartCommand { get; }

    private async Task RunAsync()
    {
        IsBusy = true;
        Logs = "--- Start Wyszukiwania ---\n";

        try
        {
            // 1. Budowanie URL
            // Pobieramy domenę z SeedUrl
            var uri = new Uri(SeedUrl);
            var domain = uri.GetLeftPart(UriPartial.Authority); // https://www.taniaksiazka.pl

            const string searchPath = "/szukaj?q=";
            var encodedQuery = WebUtility.UrlEncode(SearchQuery);

            var targetUrl = $"{domain}{searchPath}{encodedQuery}";

            AppendLog($"Szukam frazy: '{SearchQuery}'");
            AppendLog($"Pełny URL: {targetUrl}");

            // 2. Neo4j
            AppendLog("Łączenie z Neo4j...");
            await _neo4j.VerifyConnectionAsync();
            await _neo4j.InitializeConstraintsAsync();
            AppendLog("Połączono z bazą.");

            // 3. Crawling
            var books = await _crawler.RunCrawlAsync(targetUrl, MaxPages, msg => AppendLog(msg));
            AppendLog($"Znaleziono {books.Count} unikalnych książek.");

            // 4. Zapis
            if (books.Count > 0)
            {
                AppendLog("Zapisywanie do Neo4j...");
                await _neo4j.SaveBooksAsync(books);
                AppendLog("Zapisano.");

                // 5. PDF
                AppendLog("Generowanie PDF...");
                var pdfPath = _options.Report.OutputPath;
                await _pdf.GenerateReportAsync(pdfPath, targetUrl, books.Count, 0);
                AppendLog($"Raport PDF: {pdfPath}");
            }
            else
            {
                AppendLog("Brak wyników do zapisania.");
            }
        }
        catch (Exception ex)
        {
            AppendLog($"BŁĄD: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
            AppendLog("--- Zakończono ---");
        }
    }

    private void AppendLog(string message)
    {
        Logs += $"{DateTime.Now:HH:mm:ss}: {message}\n";
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public class RelayCommand(Func<object?, Task> execute, Predicate<object?>? canExecute = null) : ICommand
{
    public bool CanExecute(object? parameter) => canExecute == null || canExecute(parameter);
    public async void Execute(object? parameter) { if (execute != null) await execute(parameter); }
    public event EventHandler? CanExecuteChanged;
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
