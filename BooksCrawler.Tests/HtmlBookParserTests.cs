using BooksCrawler.Models;
using BooksCrawler.Services;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;

namespace BooksCrawler.Tests;

[TestFixture]
public class HtmlBookParserTests
{
    [Test]
    public void ParseBooksFromList_ParsesLinks_Prices_AndDeduplicatesUrls()
    {
        var logger = new Mock<ILogger<HtmlBookParser>>();
        var sut = new HtmlBookParser(logger.Object);

        var html = """
        <html><body>
          <a class="ecommerce-datalayer" href="/b1" data-name="T1" data-price="12,34" data-brand="Pub1">x</a>
          <a class="ecommerce-datalayer" href="/b1" data-name="T1" data-price="12,34" data-brand="Pub1">x</a>
          <a class="ecommerce-datalayer" href="https://site/b2" data-name="" data-price="9.99" data-brand="">Inner Title</a>
        </body></html>
        """;

        var books = sut.ParseBooksFromList(html, "https://site");

        Assert.That(books, Has.Count.EqualTo(2));
        Assert.That(books[0].Url, Is.EqualTo("https://site/b1"));
        Assert.That(books[0].Title, Is.EqualTo("T1"));
        Assert.That(books[0].Price, Is.EqualTo(12.34m));
        Assert.That(books[0].Publisher, Is.EqualTo("Pub1"));
        Assert.That(books[0].Authors, Is.Not.Null);

        Assert.That(books[1].Url, Is.EqualTo("https://site/b2"));
        Assert.That(books[1].Title, Is.EqualTo("Inner Title"));
        Assert.That(books[1].Price, Is.EqualTo(9.99m));
        Assert.That(books[1].Publisher, Is.Null);
    }

    [Test]
    public void EnrichBookDetails_ReadsAuthorsPublisherAndYear()
    {
        var logger = new Mock<ILogger<HtmlBookParser>>(); // <-- FIX: typowany logger
        var sut = new HtmlBookParser(logger.Object);

        var book = new Book { Title = "T", Url = "https://site/b1", Authors = new() };

        var detailHtml = """
        <html><body>
          <div class="product-info-author">Autor: Jan Kowalski, Adam Nowak</div>
          <table>
            <tr><th>Wydawnictwo</th><td>XYZ</td></tr>
            <tr><th>Rok wydania</th><td>Premiera 2024</td></tr>
          </table>
        </body></html>
        """;

        sut.EnrichBookDetails(book, detailHtml);

        Assert.That(book.Authors, Is.EquivalentTo(new[] { "Jan Kowalski", "Adam Nowak" }));
        Assert.That(book.Publisher, Is.EqualTo("XYZ"));
        Assert.That(book.Year, Is.EqualTo(2024));
    }

    [Test]
    public void ParseNextPageLink_ReturnsAbsoluteUrl()
    {
        var logger = new Mock<ILogger<HtmlBookParser>>(); // <-- FIX: typowany logger
        var sut = new HtmlBookParser(logger.Object);

        var html = """<html><body><li class="next"><a href="/page2">Next</a></li></body></html>""";
        var next = sut.ParseNextPageLink(html, "https://site");

        Assert.That(next, Is.EqualTo("https://site/page2"));
    }
}
