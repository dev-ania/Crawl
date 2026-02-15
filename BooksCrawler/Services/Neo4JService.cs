using Neo4j.Driver;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using BooksCrawler.Configuration;
using BooksCrawler.Models;
using System.Linq;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace BooksCrawler.Services;

public sealed class Neo4JService : IAsyncDisposable
{
    private readonly IDriver _driver;
    private readonly ILogger _logger;

    public Neo4JService(IOptions<AppOptions> options, ILogger<Neo4JService> logger)
    {
        _logger = logger;
        var config = options.Value.Neo4J;
        _driver = GraphDatabase.Driver(config.Uri, AuthTokens.Basic(config.User, config.Password));
    }

    public async Task SaveBookAsync(Book book)
    {
        await using var session = _driver.AsyncSession();
        await session.ExecuteWriteAsync(async tx =>
        {
            var query = @"
            MERGE (b:Book {id: $id})
            SET b.title = $title, b.price = $price, b.url = $url, b.updatedAt = datetime(), 
                b.year = $year
            WITH b
            FOREACH (authName IN $authors | MERGE (a:Author {name: authName}) MERGE (a)-[:WROTE]->(b))
            WITH b
            CALL apoc.do.when($publisher IS NOT NULL,
                'MERGE (p:Publisher {name: publisher}) MERGE (b)-[:PUBLISHED_BY]->(p)', '', {b:b, publisher:$publisher}) YIELD value
            RETURN b";

            await tx.RunAsync(query, new
            {
                id = book.Id,
                title = book.Title,
                price = book.Price,
                url = book.Url,
                year = book.Year,
                authors = book.Authors ?? new List<string>(),
                publisher = book.Publisher
            });
        });
    }

    public async Task<List<Book>> GetRecentBooksAsync(int limit = 20)
    {
        await using var session = _driver.AsyncSession();
        return await session.ExecuteReadAsync(async tx =>
        {
            var cursor = await tx.RunAsync(@"
            MATCH (b:Book) WITH b ORDER BY b.updatedAt DESC LIMIT $limit
            OPTIONAL MATCH (a:Author)-[:WROTE]->(b)
            RETURN b.title as title, b.price as price, b.url as url, collect(a.name) as authors", new { limit });
            return await MapCursorToBooks(cursor);
        });
    }

    public async Task<Dictionary<string, string>> GetStatisticsAsync()
    {
        await using var session = _driver.AsyncSession();
        return await session.ExecuteReadAsync(async tx =>
        {
            var res = new Dictionary<string, string>();

            // Liczba książek
            var c = await tx.RunAsync("MATCH (n:Book) RETURN count(n) as c");
            if (await c.FetchAsync())
                res["Liczba książek w bazie"] = c.Current["c"].As<long>().ToString();

            // Min/Avg/Max ceny
            var priceStats = await tx.RunAsync(@"
                MATCH (b:Book) WHERE b.price IS NOT NULL 
                RETURN min(b.price) as minPrice, avg(b.price) as avgPrice, max(b.price) as maxPrice");
            if (await priceStats.FetchAsync())
            {
                res["Cena minimalna"] = priceStats.Current["minPrice"].As<decimal>().ToString("F2") + " PLN";
                res["Cena średnia"] = priceStats.Current["avgPrice"].As<double>().ToString("F2") + " PLN";
                res["Cena maksymalna"] = priceStats.Current["maxPrice"].As<decimal>().ToString("F2") + " PLN";
            }

            // Top 5 autorów
            var topAuthors = await tx.RunAsync(@"
                MATCH (a:Author)-[:WROTE]->(b:Book)
                RETURN a.name as author, count(b) as cnt
                ORDER BY cnt DESC LIMIT 5");
            var authorsList = new List<string>();
            while (await topAuthors.FetchAsync())
            {
                var name = topAuthors.Current["author"].As<string>();
                var cnt = topAuthors.Current["cnt"].As<long>();
                authorsList.Add($"{name} ({cnt})");
            }
            res["Top 5 autorów"] = string.Join(", ", authorsList);

            return res;
        });
    }

    // --- ANALIZY (METODY DLA UI I RAPORTU) ---
    // Wszystkie parametry mają wartości domyślne

    // 1. RANKINGI CENOWE (z opcjonalnym filtrem ceny maksymalnej)
    public async Task<List<string>> GetBooksByPriceQueryAsync(
        string sortOrder = "DESC",
        decimal? maxPrice = null,
        int limit = 10)
    {
        var where = maxPrice.HasValue ? "WHERE b.price < $maxPrice" : "WHERE b.price IS NOT NULL";
        var query = $@"
        MATCH (b:Book) {where}
        OPTIONAL MATCH (a:Author)-[:WROTE]->(b)
        WITH b, collect(a.name) as authors
        ORDER BY b.price {sortOrder} LIMIT $limit
        RETURN b.title + ' - ' + 
        CASE WHEN size(authors)>0 THEN reduce(s = '', author IN authors | s + CASE WHEN s = '' THEN author ELSE ', ' + author END) ELSE 'Nieznany' END + 
        ' (' + toString(b.price) + ' PLN)' AS result";
        return await ExecuteSimpleQuery(query, new { maxPrice, limit });
    }

    // 2. FILTR PO ROKU WYDANIA
    public async Task<List<string>> GetNewReleasesAsync(
        int minYear = 2020,
        int limit = 10)
    {
        var query = @"
        MATCH (b:Book) WHERE b.year >= $minYear
        OPTIONAL MATCH (a:Author)-[:WROTE]->(b)
        WITH b, collect(a.name) as authors
        ORDER BY b.year DESC
        LIMIT $limit
        RETURN b.title + ' - ' + 
        CASE WHEN size(authors)>0 THEN reduce(s = '', author IN authors | s + CASE WHEN s = '' THEN author ELSE ', ' + author END) ELSE 'Nieznany' END + 
        ' (Rok ' + toString(b.year) + ')' AS result";
        return await ExecuteSimpleQuery(query, new { minYear, limit });
    }

    // 3. AGREGACJA - Średnia cena autora (z minimalną liczbą książek)
    public async Task<List<string>> GetAuthorsByAvgPriceAsync(
        int limit = 10,
        int minBookCount = 1)
    {
        var query = @"
        MATCH (a:Author)-[:WROTE]->(b:Book) WHERE b.price IS NOT NULL
        WITH a, avg(b.price) as avgPrice, count(b) as cnt
        WHERE cnt >= $minBookCount
        RETURN 'Średnia - ' + a.name + ' (' + toString(round(avgPrice*100)/100.0) + ' PLN)' AS result
        ORDER BY avgPrice DESC LIMIT $limit";
        return await ExecuteSimpleQuery(query, new { limit, minBookCount });
    }

    // 4. RELACJA - Top wydawnictwa (z minimalną liczbą książek)
    public async Task<List<string>> GetTopPublishersAsync(
        int limit = 10,
        int minBookCount = 1)
    {
        var query = @"
        MATCH (p:Publisher)<-[:PUBLISHED_BY]-(b:Book)
        WITH p, count(b) as cnt
        WHERE cnt >= $minBookCount
        RETURN p.name + ' (' + toString(cnt) + ' książek)' AS result
        ORDER BY cnt DESC LIMIT $limit";
        return await ExecuteSimpleQuery(query, new { limit, minBookCount });
    }

    // 5. AUTORZY - Ranking wg liczby książek (z minimalną liczbą)
    public async Task<List<string>> GetTopAuthorsByCountAsync(
        int limit = 10,
        int minBookCount = 1)
    {
        var query = @"
        MATCH (a:Author)-[:WROTE]->(b:Book)
        WITH a, count(b) as cnt
        WHERE cnt >= $minBookCount
        RETURN a.name + ' (' + toString(cnt) + ' książek)' AS result
        ORDER BY cnt DESC LIMIT $limit";
        return await ExecuteSimpleQuery(query, new { limit, minBookCount });
    }

    // 6. AUTORZY - Książki wydane po określonym roku
    public async Task<List<string>> GetAuthorsByYearAsync(
        int minYear = 2020,
        int limit = 10)
    {
        var query = @"
        MATCH (a:Author)-[:WROTE]->(b:Book)
        WHERE b.year >= $minYear
        WITH a, b
        ORDER BY b.year DESC, a.name ASC
        LIMIT $limit
        RETURN b.title + ' - ' + a.name + ' (Rok ' + toString(b.year) + ')' AS result";
        return await ExecuteSimpleQuery(query, new { minYear, limit });
    }

    // --- GLÓWNA METODA RAPORTU (Zbiera wszystko z domyślnymi wartościami) ---
    public async Task<List<AnalysisResult>> RunAnalysisAsync(
        int topLimit = 10,
        decimal defaultMaxPrice = 50m,
        int defaultMinYear = 2020)
    {
        var results = new List<AnalysisResult>();

        // 1. Rankingi podstawowe
        results.Add(new($"Top {topLimit} Najdroższych",
            await GetBooksByPriceQueryAsync("DESC", null, topLimit),
            $"Limit: {topLimit}"));

        results.Add(new($"Top {topLimit} Najtańszych",
            await GetBooksByPriceQueryAsync("ASC", null, topLimit),
            $"Limit: {topLimit}"));

        // 2. Filtr cenowy
        results.Add(new($"Tańsze niż {defaultMaxPrice} PLN",
            await GetBooksByPriceQueryAsync("ASC", defaultMaxPrice, topLimit),
            $"Limit: {topLimit}, Cena < {defaultMaxPrice} PLN"));

        // 3. Filtr roku
        results.Add(new($"Wydane po roku {defaultMinYear}",
            await GetNewReleasesAsync(defaultMinYear, topLimit),
            $"Limit: {topLimit}, Rok >= {defaultMinYear}"));

        // 4. Agregacja - średnia cena autora
        results.Add(new($"Autorzy (Średnia Cena)",
            await GetAuthorsByAvgPriceAsync(topLimit, 1),
            $"Limit: {topLimit}, Min. 1 książka"));

        // 5. Relacja - wydawcy
        results.Add(new($"Top {topLimit} Wydawnictw",
            await GetTopPublishersAsync(topLimit, 1),
            $"Limit: {topLimit}, Min. 1 książka"));

        // 6. Autorzy wg liczby
        results.Add(new($"Top {topLimit} Autorów (Liczba)",
            await GetTopAuthorsByCountAsync(topLimit, 1),
            $"Limit: {topLimit}, Min. 1 książka"));

        // 7. Autorzy - książki wydane po roku X
        results.Add(new($"Autorzy - Książki po roku {defaultMinYear}",
            await GetAuthorsByYearAsync(defaultMinYear, topLimit),
            $"Limit: {topLimit}, Rok >= {defaultMinYear}"));

        return results;
    }

    // --- Helpers ---
    private async Task<List<string>> ExecuteSimpleQuery(string cypher, object? param)
    {
        await using var session = _driver.AsyncSession();
        try
        {
            var cursor = await session.RunAsync(cypher, param);
            return (await cursor.ToListAsync()).Select(r => r["result"].As<string>()).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Błąd Cypher: {Cypher}", cypher);
            return new List<string> { "Błąd: " + ex.Message };
        }
    }

    private async Task<List<Book>> MapCursorToBooks(IResultCursor cursor)
    {
        var list = new List<Book>();
        while (await cursor.FetchAsync())
        {
            var r = cursor.Current;
            list.Add(new Book
            {
                Title = r["title"].As<string>(),
                Url = r["url"].As<string>(),
                Authors = r["authors"].As<List<string>>()
            });
        }
        return list;
    }

    public ValueTask DisposeAsync() => _driver.DisposeAsync();

    public record AnalysisResult(string QueryName, List<string> Results, string? Parameters = null);
}
