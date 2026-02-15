using BooksCrawler.Configuration;
using BooksCrawler.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Neo4j.Driver;

namespace BooksCrawler.Services;

// Użycie Primary Constructor
public sealed class Neo4JService(IOptions<AppOptions> options, ILogger<Neo4JService> logger) : IAsyncDisposable
{
    private readonly IDriver _driver = GraphDatabase.Driver(
        options.Value.Neo4J.Uri,
        AuthTokens.Basic(options.Value.Neo4J.User, options.Value.Neo4J.Password));

    public async Task VerifyConnectionAsync()
    {
        await _driver.VerifyConnectivityAsync();
        logger.LogInformation("Połączono z Neo4j pomyślnie.");
    }

    public async Task InitializeConstraintsAsync()
    {
        await using var session = _driver.AsyncSession();
        await session.ExecuteWriteAsync(async tx =>
        {
            await tx.RunAsync("CREATE CONSTRAINT IF NOT EXISTS FOR (b:Book) REQUIRE b.id IS UNIQUE");
        });
    }

    public async Task SaveBooksAsync(IEnumerable<Book> books)
    {
        await using var session = _driver.AsyncSession();

        // Batch insert - good practice w Neo4j
        var query = @"
            UNWIND $batch AS row
            MERGE (b:Book {id: row.Id})
            SET b.title = row.Title, 
                b.price = row.Price, 
                b.url = row.Url";

        // Konwersja do mapy parametrów
        var parameters = new { batch = books.Select(b => new { b.Id, b.Title, b.Price, b.Url }).ToList() };

        await session.ExecuteWriteAsync(async tx =>
        {
            await tx.RunAsync(query, parameters);
        });

        logger.LogInformation("Zapisano {Count} książek w transakcji.", books.Count());
    }

    public ValueTask DisposeAsync() => _driver.DisposeAsync();
}