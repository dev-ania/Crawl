using BooksCrawler.Configuration;
using BooksCrawler.Services;
using BooksCrawler.ViewModels;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Windows;

namespace BooksCrawler;

public partial class App : Application
{
    private readonly IHost _host;

    public App()
    {
        _host = Host.CreateApplicationBuilder().Build();

        var builder = Host.CreateApplicationBuilder();

        // Config
        builder.Configuration.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
        builder.Configuration.AddUserSecrets<App>();

        // Services
        builder.Services.Configure<AppOptions>(builder.Configuration);
        builder.Services.AddHttpClient<BookCrawlerService>();

        builder.Services.AddSingleton<Neo4JService>();
        builder.Services.AddTransient<HtmlBookParser>();
        builder.Services.AddTransient<DuplicateDetector>();
        builder.Services.AddTransient<PdfReportService>();

        // Windows
        builder.Services.AddSingleton<MainViewModel>();
        builder.Services.AddSingleton<MainWindow>();

        _host = builder.Build();
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        await _host.StartAsync();

        var mainWindow = _host.Services.GetRequiredService<MainWindow>();
        mainWindow.Show();

        base.OnStartup(e);
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        await _host.StopAsync();
        _host.Dispose();
        base.OnExit(e);
    }
}