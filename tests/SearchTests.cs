using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace BenScr.TextMatching.Tests;

public sealed class SearchTests
{
    [Fact]
    public void ResultsSortByDistanceThenInputIndexAndRetainDuplicateValues()
    {
        string[] candidates = { "bat", "cat", "cut", "cat", "dog", "cats" };
        IReadOnlyList<TextMatch> matches = new TextMatcher().FindClosest("cat", candidates, 1, 4);
        Assert.Equal(new[] { 1, 3, 0, 2 }, matches.Select(match => match.Index));
        Assert.Equal(new[] { 0, 0, 1, 1 }, matches.Select(match => match.Distance));
        Assert.Equal(new[] { "cat", "cat", "bat", "cut" }, matches.Select(match => match.Candidate));
    }

    [Fact]
    public void BetterLateResultReplacesWorstLateTie()
    {
        IReadOnlyList<TextMatch> matches = new TextMatcher().FindClosest("cat", new[] { "bat", "cut", "cat" }, 1, 2);
        Assert.Equal(new[] { 2, 0 }, matches.Select(match => match.Index));
    }

    [Fact]
    public void EmptyQueryMatchesByScalarLengthAndEmptySequencesAreValid()
    {
        var matcher = new TextMatcher();
        Assert.Empty(matcher.FindClosest("query", Array.Empty<string>(), 2));
        Assert.Empty(matcher.FindClosest("query", new[] { "else" }, 0));
        Assert.Equal(new[] { 1, 0 }, matcher.FindClosest("", new[] { "😀", "", "ab" }, 1).Select(match => match.Index));
    }

    [Fact]
    public void ResultsCannotBeMutatedAndDoNotObserveLaterArrayChanges()
    {
        string original = new(new[] { 'c', 'a', 't' });
        string[] candidates = { original };
        IReadOnlyList<TextMatch> matches = new TextMatcher().FindClosest("cat", candidates, 0);
        candidates[0] = "changed";
        Assert.Same(original, matches[0].Candidate);
        var list = Assert.IsAssignableFrom<IList<TextMatch>>(matches);
        Assert.Throws<NotSupportedException>(() => list.Clear());
    }

    [Fact]
    public void SearchValidatesArgumentsAndEveryCandidateEvenAfterEnoughMatches()
    {
        var matcher = new TextMatcher(maxScalarLength: 3, maxCandidates: 10);
        Assert.Throws<ArgumentNullException>(() => matcher.FindClosest(null!, Array.Empty<string>(), 0));
        Assert.Throws<ArgumentNullException>(() => matcher.FindClosest("", null!, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => matcher.FindClosest("", Array.Empty<string>(), -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => matcher.FindClosest("", Array.Empty<string>(), 0, 0));
        foreach (string invalid in new[] { null!, "\ud800", "long" })
        {
            Assert.Throws<ArgumentException>(() => matcher.FindClosest("cat", new[] { "cat", invalid }, 0, 1));
        }
    }

    [Fact]
    public void SmallCandidateLimitWorksWithDefaultOrVeryLargeResultCount()
    {
        var matcher = new TextMatcher(maxCandidates: 3);
        string[] candidates = { "cat", "bat", "cut" };
        Assert.Equal(3, matcher.FindClosest("cat", candidates, 1).Count);
        Assert.Equal(3, matcher.FindClosest("cat", candidates, 1, int.MaxValue).Count);
    }

    [Fact]
    public void IteratorIsEnumeratedOnceAndDisposedOnNormalCompletion()
    {
        int enumerations = 0;
        bool disposed = false;
        IEnumerable<string> Candidates()
        {
            enumerations++;
            try
            {
                yield return "cat";
                yield return "bat";
            }
            finally
            {
                disposed = true;
            }
        }
        Assert.Equal(2, new TextMatcher().FindClosest("cat", Candidates(), 1).Count);
        Assert.Equal(1, enumerations);
        Assert.True(disposed);
    }

    [Fact]
    public void CandidateLimitStopsInfiniteIteratorAfterOneExtraElementAndDisposesIt()
    {
        int yielded = 0;
        bool disposed = false;
        IEnumerable<string> Candidates()
        {
            try
            {
                while (true)
                {
                    yielded++;
                    yield return "a";
                }
            }
            finally
            {
                disposed = true;
            }
        }
        Assert.Throws<InvalidOperationException>(() => new TextMatcher(maxCandidates: 3).FindClosest("a", Candidates(), 0, 1));
        Assert.Equal(4, yielded);
        Assert.True(disposed);
        Assert.Equal(3, new TextMatcher(maxCandidates: 3).FindClosest("a", new[] { "a", "a", "a" }, 0, 3).Count);
    }

    [Fact]
    public void IteratorExceptionsPropagateAndDisposeWithoutPartialResults()
    {
        var failure = new FormatException("Source failed.");
        bool disposed = false;
        IEnumerable<string> Candidates()
        {
            try
            {
                yield return "a";
                throw failure;
            }
            finally
            {
                disposed = true;
            }
        }
        Assert.Same(failure, Assert.Throws<FormatException>(() => new TextMatcher().FindClosest("a", Candidates(), 0)));
        Assert.True(disposed);
    }

    [Fact]
    public void ValidationAndBudgetFailuresDisposeTheInputIterator()
    {
        foreach (string candidate in new[] { "\ud800", "aaa" })
        {
            bool disposed = false;
            IEnumerable<string> Candidates()
            {
                try
                {
                    yield return candidate;
                }
                finally
                {
                    disposed = true;
                }
            }
            Assert.ThrowsAny<Exception>(() => new TextMatcher(maxCellUpdates: 1).FindClosest("aaa", Candidates(), 3));
            Assert.True(disposed);
        }
    }

    [Fact]
    public void FixedSeedSearchMatchesIndependentFullSortReference()
    {
        var random = new Random(982151);
        var matcher = new TextMatcher();
        for (int iteration = 0; iteration < 500; iteration++)
        {
            string query = DistanceTests.RandomText(random, random.Next(8));
            string[] candidates = Enumerable.Range(0, random.Next(30))
                .Select(_ => DistanceTests.RandomText(random, random.Next(8))).ToArray();
            int threshold = random.Next(8);
            int take = random.Next(1, 12);
            var expected = candidates.Select((text, index) => (Index: index, Distance: DistanceTests.ReferenceDistance(query, text)))
                .Where(match => match.Distance <= threshold).OrderBy(match => match.Distance).ThenBy(match => match.Index).Take(take).ToArray();
            IReadOnlyList<TextMatch> actual = matcher.FindClosest(query, candidates, threshold, take);
            Assert.Equal(expected, actual.Select(match => (match.Index, match.Distance)));
        }
    }
}
