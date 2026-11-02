using System;
using System.Linq;
using System.Text;
using BenScr.TextMatching;

var matcher = new TextMatcher();
Require(matcher.Distance("kitten", "sitting") == 3, "Known edit distance");
Require(matcher.Distance("😀", "") == 1, "Supplementary character is one scalar");
Require(matcher.DistanceWithin("kitten", "sitting", 2) is null, "Threshold rejection");
Require(matcher.DistanceWithin("kitten", "sitting", 3) == 3, "Inclusive threshold");
Require(matcher.Distance("e\u0301".Normalize(NormalizationForm.FormC), "é") == 0, "Explicit caller normalization");

var matches = matcher.FindClosest("cat", new[] { "bat", "cat", "cut", "cat", "dog" }, maxDistance: 1, maxResults: 3);
Require(matches.Select(match => match.Index).SequenceEqual(new[] { 1, 3, 0 }), "Distance and stable input order");
foreach (TextMatch match in matches)
{
    Console.WriteLine($"{match.Candidate}: distance {match.Distance}, index {match.Index}");
}

var bounded = new TextMatcher(maxScalarLength: 100, maxCellUpdates: 100);
Require(bounded.DistanceWithin(new string('x', 100), new string('x', 100), 0) == 0, "Narrow-band budget");
bool rejected = false;
try
{
    matcher.Distance(new string((char)0xd800, 1), "");
}
catch (ArgumentException)
{
    rejected = true;
}
Require(rejected, "Invalid UTF-16 rejected");
Console.WriteLine("TextMatching package consumer scenarios passed.");

static void Require(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}
