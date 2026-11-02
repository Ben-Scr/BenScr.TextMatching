using System;
using Xunit;

namespace BenScr.TextMatching.Tests;

public sealed class LimitsTests
{
    [Theory]
    [InlineData(0, 1, 1)]
    [InlineData(1_000_001, 1, 1)]
    [InlineData(1, 0, 1)]
    [InlineData(1, 1_000_001, 1)]
    [InlineData(1, 1, 0)]
    [InlineData(1, 1, -1)]
    public void InvalidLimitsAreRejected(int length, int candidates, long updates)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TextMatcher(length, candidates, updates));
    }

    [Fact]
    public void LimitConfigurationIsRetainedWithoutAllocatingItsFullCapacity()
    {
        var matcher = new TextMatcher(1_000_000, 1_000_000, long.MaxValue);
        Assert.Equal(1_000_000, matcher.MaxScalarLength);
        Assert.Equal(1_000_000, matcher.MaxCandidates);
        Assert.Equal(long.MaxValue, matcher.MaxCellUpdates);
        Assert.Equal(0, matcher.Distance("", ""));
    }

    [Fact]
    public void LengthBoundsCountScalarsAndAreInclusive()
    {
        var matcher = new TextMatcher(maxScalarLength: 2);
        Assert.Equal(2, matcher.Distance("😀😀", ""));
        Assert.Equal(2, matcher.Distance("ab", ""));
        Assert.Throws<ArgumentException>(() => matcher.Distance("abc", ""));
        Assert.Throws<ArgumentException>(() => matcher.Distance("😀😀😀", ""));
        Assert.Throws<ArgumentException>(() => matcher.DistanceWithin("", "abc", 0));
    }

    [Fact]
    public void ExactDistanceBudgetHasInclusiveCellBoundary()
    {
        Assert.Equal(3, new TextMatcher(maxCellUpdates: 42).Distance("kitten", "sitting"));
        Assert.Throws<InvalidOperationException>(() => new TextMatcher(maxCellUpdates: 41).Distance("kitten", "sitting"));
    }

    [Fact]
    public void NarrowBandFitsBudgetThatRejectsFullMatrix()
    {
        string text = new('x', 100);
        var matcher = new TextMatcher(maxCellUpdates: 100);
        Assert.Equal(0, matcher.DistanceWithin(text, text, 0));
        Assert.Throws<InvalidOperationException>(() => matcher.Distance(text, text));
        Assert.Throws<InvalidOperationException>(() => new TextMatcher(maxCellUpdates: 99).DistanceWithin(text, text, 0));
        Assert.Null(new TextMatcher(maxCellUpdates: 1).DistanceWithin("abc", "", 0));
        Assert.Equal(3, new TextMatcher(maxCellUpdates: 1).Distance("abc", ""));
    }

    [Fact]
    public void SearchSharesBudgetAcrossAllCandidatesAndResetsNextCall()
    {
        string[] candidates = { "ab", "ab", "ab" };
        Assert.Single(new TextMatcher(maxCellUpdates: 6).FindClosest("ab", candidates, 0, 1));
        var matcher = new TextMatcher(maxCellUpdates: 5);
        Assert.Throws<InvalidOperationException>(() => matcher.FindClosest("ab", candidates, 0, 1));
        Assert.Equal(0, matcher.Distance("ab", "ab"));
    }
}
