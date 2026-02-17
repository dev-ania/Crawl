using BooksCrawler.Services;
using NUnit.Framework;

namespace BooksCrawler.Tests;

[TestFixture]
public class DuplicateDetectorTests
{
    [Test]
    public void IsDuplicate_FirstTime_ReturnsFalse_AndCountsUnique()
    {
        var sut = new DuplicateDetector();

        var isDup = sut.IsDuplicate("https://example.com/a");

        Assert.That(isDup, Is.False);
        Assert.That(sut.UniqueCount, Is.EqualTo(1));
        Assert.That(sut.RejectedCount, Is.EqualTo(0));
    }

    [Test]
    public void IsDuplicate_SecondTime_ReturnsTrue_AndIncrementsRejected()
    {
        var sut = new DuplicateDetector();

        _ = sut.IsDuplicate("https://example.com/a");
        var isDup = sut.IsDuplicate("https://example.com/a");

        Assert.That(isDup, Is.True);
        Assert.That(sut.UniqueCount, Is.EqualTo(1));
        Assert.That(sut.RejectedCount, Is.EqualTo(1));
    }
}