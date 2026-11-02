using System;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace BenScr.TextMatching.Tests;

public sealed class DistanceTests
{
    [Theory]
    [InlineData("", "", 0)]
    [InlineData("", "abc", 3)]
    [InlineData("kitten", "sitting", 3)]
    [InlineData("Saturday", "Sunday", 3)]
    [InlineData("book", "back", 2)]
    [InlineData("ab", "ba", 2)]
    [InlineData("same", "same", 0)]
    [InlineData("A", "a", 1)]
    [InlineData("😀", "", 1)]
    [InlineData("😀", "😃", 1)]
    [InlineData("a😀z", "az", 1)]
    [InlineData("e\u0301", "é", 2)]
    [InlineData("👨‍👩‍👧", "", 5)]
    [InlineData("\0\n", "\0\r", 1)]
    [InlineData("𐐀", "𐐨", 1)]
    [InlineData("Straße", "STRASSE", 6)]
    public void KnownDistancesAreScalarBased(string left, string right, int expected)
    {
        var matcher = new TextMatcher();
        Assert.Equal(expected, matcher.Distance(left, right));
        Assert.Equal(expected, matcher.Distance(right, left));
        Assert.Equal(expected, matcher.DistanceWithin(left, right, expected));
        Assert.Equal(expected, matcher.DistanceWithin(left, right, int.MaxValue));
        if (expected > 0)
        {
            Assert.Null(matcher.DistanceWithin(left, right, expected - 1));
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void IllFormedUtf16IsRejectedOnBothSidesAndBeforeLengthShortcut(int kind)
    {
        // Construct after discovery: attribute strings pass through UTF-8 metadata encoding.
        string invalid = kind switch
        {
            0 => new string((char)0xd800, 1),
            1 => new string((char)0xdfff, 1),
            2 => new string(new[] { 'a', (char)0xd800, 'b' }),
            3 => new string((char)0xd800, 2),
            _ => new string(new[] { (char)0xdc00, (char)0xd800 })
        };
        var matcher = new TextMatcher();
        Assert.Throws<ArgumentException>(() => matcher.Distance(invalid, "valid"));
        Assert.Throws<ArgumentException>(() => matcher.Distance("valid", invalid));
        Assert.Throws<ArgumentException>(() => matcher.DistanceWithin(invalid, "", 0));
        Assert.Throws<ArgumentException>(() => matcher.DistanceWithin("", invalid, 0));
    }

    [Fact]
    public void ReplacementCharacterIsOrdinaryValidData()
    {
        var matcher = new TextMatcher();
        Assert.Equal(0, matcher.Distance("\ufffd", "\ufffd"));
        Assert.Equal(1, matcher.Distance("\ufffd", ""));
    }

    [Fact]
    public void NullAndNegativeThresholdsHaveConsistentArgumentErrors()
    {
        var matcher = new TextMatcher();
        Assert.Throws<ArgumentNullException>(() => matcher.Distance(null!, ""));
        Assert.Throws<ArgumentNullException>(() => matcher.Distance("", null!));
        Assert.Throws<ArgumentNullException>(() => matcher.DistanceWithin(null!, "", 0));
        Assert.Throws<ArgumentNullException>(() => matcher.DistanceWithin("", null!, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => matcher.DistanceWithin("", "", -1));
    }

    [Fact]
    public void FixedSeedDifferentialTestUsesFullMatrixReference()
    {
        var random = new Random(41983);
        var matcher = new TextMatcher();
        for (int iteration = 0; iteration < 2500; iteration++)
        {
            string left = RandomText(random, random.Next(18));
            string right = RandomText(random, random.Next(18));
            int expected = ReferenceDistance(left, right);
            Assert.Equal(expected, matcher.Distance(left, right));
            for (int threshold = 0; threshold <= 18; threshold++)
            {
                int? bounded = expected <= threshold ? expected : null;
                Assert.Equal(bounded, matcher.DistanceWithin(left, right, threshold));
                Assert.Equal(bounded, matcher.DistanceWithin(right, left, threshold));
            }
        }
    }

    [Fact]
    public void NarrowBandHandlesLongPrefixesAndMovingBandEdges()
    {
        var matcher = new TextMatcher(maxCellUpdates: 20_000);
        string prefix = new('a', 2000);
        Assert.Equal(1, matcher.DistanceWithin(prefix + "b", "c" + prefix + "b", 1));
        Assert.Equal(2, matcher.DistanceWithin(prefix + "bc", "d" + prefix + "b", 2));
        Assert.Null(matcher.DistanceWithin(prefix + "bc", "d" + prefix + "b", 1));
    }

    [Fact]
    public void ComparisonDoesNotDependOnCurrentCulture()
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            Assert.Equal(1, new TextMatcher().Distance("I", "ı"));
            Assert.Equal(1, new TextMatcher().Distance("i", "İ"));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void MatcherCanBeUsedConcurrentlyWithoutSharedBudgets()
    {
        var matcher = new TextMatcher(maxCellUpdates: 42);
        Parallel.For(0, 100, _ => Assert.Equal(3, matcher.Distance("kitten", "sitting")));
    }

    internal static string RandomText(Random random, int count)
    {
        string[] alphabet = { "a", "b", "c", "\u0301", "é", "😀", "𐐀", "\0" };
        var text = new StringBuilder();
        for (int index = 0; index < count; index++)
        {
            text.Append(alphabet[random.Next(alphabet.Length)]);
        }
        return text.ToString();
    }

    internal static int ReferenceDistance(string left, string right)
    {
        // Full matrix over Unicode scalar strings, independent of the production band and buffers.
        string[] a = left.EnumerateRunes().Select(rune => rune.ToString()).ToArray();
        string[] b = right.EnumerateRunes().Select(rune => rune.ToString()).ToArray();
        var costs = new int[a.Length + 1, b.Length + 1];
        for (int i = 0; i <= a.Length; i++)
        {
            costs[i, 0] = i;
        }
        for (int j = 0; j <= b.Length; j++)
        {
            costs[0, j] = j;
        }
        for (int i = 1; i <= a.Length; i++)
        {
            for (int j = 1; j <= b.Length; j++)
            {
                costs[i, j] = new[] { costs[i - 1, j] + 1, costs[i, j - 1] + 1,
                    costs[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1) }.Min();
            }
        }
        return costs[a.Length, b.Length];
    }
}
