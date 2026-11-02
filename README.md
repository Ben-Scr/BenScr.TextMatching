# BenScr.TextMatching

Unicode-scalar Levenshtein distance and bounded fuzzy search for .NET 10. Find close spellings,
rank suggestions, or reject strings outside an edit-distance threshold. No runtime package dependencies.

Created by Ben Schneider — https://github.com/Ben-Scr

```shell
dotnet add package BenScr.TextMatching --version 1.0.0
```

```csharp
using BenScr.TextMatching;

var matcher = new TextMatcher();
int distance = matcher.Distance("kitten", "sitting"); // 3
int? nearby = matcher.DistanceWithin("kitten", "sitting", maxDistance: 2); // null

var matches = matcher.FindClosest(
    "cat", new[] { "bat", "cat", "cut", "cat", "dog" },
    maxDistance: 1, maxResults: 3);
// Candidate / distance / original index: cat / 0 / 1, cat / 0 / 3, bat / 1 / 0.
```

## Comparison contract

Insertion, deletion and substitution each cost one. Adjacent transposition is two edits, not one.
`Distance` returns an exact integer. `DistanceWithin` returns that same exact integer when it is
at most the inclusive threshold, otherwise null. A zero threshold tests exact scalar equality;
negative thresholds are invalid and `int.MaxValue` is supported without arithmetic overflow.
Both inputs are validated before a length-based rejection.

Comparison uses Unicode scalar values, including supplementary-plane characters, rather than UTF-16
code units. For example, deleting `😀` costs one edit. A visible grapheme can contain multiple scalars:
`e` followed by a combining acute accent has distance two from precomposed `é`. Comparison is ordinal,
case-sensitive, culture-independent, and performs no normalization, trimming, case folding or accent
removal. Apply a consistent transformation explicitly before calling if your application needs one:

```csharp
using System.Text;
int normalized = matcher.Distance("e\u0301".Normalize(NormalizationForm.FormC), "é"); // 0
```

Null strings throw `ArgumentNullException`. Unpaired UTF-16 surrogates throw `ArgumentException`;
they are never silently replaced. The valid replacement character U+FFFD remains ordinary data.
Empty strings, NUL, line breaks and valid noncharacters are accepted as scalar values. This package
does not perform linguistic comparison or measure perceived visual similarity.

## Search ordering and ownership

`FindClosest` enumerates its source once and validates every candidate, including candidates that
cannot improve an already full result set. Matches sort by increasing distance, then increasing
zero-based input index. Duplicate strings remain separate candidates. Later ties never displace
earlier ties. No match produces an empty result collection.

The returned collection and each result are read-only, with original immutable string references.
Changing the source array afterward does not change prior results. The source enumerator is disposed
on normal completion and failure. Null candidates, malformed text and overlong strings are errors.
Iterator exceptions propagate unchanged; a failure returns no partial results. A stateful or blocking
enumerator remains the caller's responsibility. There is no background execution or timeout.

Matcher configuration is immutable. Calls have independent working buffers and budgets, so one matcher
can be reused concurrently. Concurrent use of the input sequence requires a suitable sequence supplied
by the caller. Input strings are not retained by distance calls; search results retain their candidates.

## Resource limits

All limits are inclusive and configurable in the constructor:

| Setting | Default | Allowed values | Scope |
| --- | ---: | --- | --- |
| `MaxScalarLength` | 4,096 | 1–1,000,000 | Each query, candidate or distance input |
| `MaxCandidates` | 10,000 | 1–1,000,000 | Entire search input, including nonmatches |
| `MaxCellUpdates` | 10,000,000 | 1–`long.MaxValue` | Recurrence cells per public call; shared across a search |

`maxResults` must be positive; its default is ten. Values larger than `MaxCandidates` are accepted;
the candidate limit still bounds the actual result count. Excess string length throws
`ArgumentException`. Excess candidates or computation throw `InvalidOperationException`. To detect
an overlong sequence, search requests at most `MaxCandidates + 1` elements. The extra element is not
compared. Limits are checked again independently on the next call, including after failure.

Exact distance uses two rolling rows with one recurrence update per scalar pair. Bounded distance
only evaluates the diagonal band and can stop when a row cannot reach the threshold. At threshold
zero, two equal strings of length 100 require 100 updates, versus 10,000 for exact distance. Length
rejections and comparisons with an empty string require no recurrence updates. Budget checks occur
before evaluating a row. No candidate-dependent threshold tightening changes search budgets.

Decoding, row initialization, heap operations and result materialization are outside the recurrence
budget; their sizes are bounded by the other settings. These are deterministic resource guards,
not a wall-clock or total byte-allocation guarantee. Inputs longer than twice the scalar limit in
UTF-16 units are rejected immediately; length errors may take precedence over malformed text.
Distance uses O(m+n) working memory. Search retains only the query, current candidate, row buffers and
at most `maxResults` matches, plus the final read-only collection. No entire candidate list is copied.

## Build and verify

Install .NET SDK 10.0.303 and PowerShell 7, then run from a clean committed checkout:

```powershell
pwsh -File tools/Verify.ps1
```

Verification builds all projects, checks formatting and the reviewed API baseline, runs the example
and tests, audits package metadata/icon/license/Portable PDBs/Source Link, and runs an isolated consumer
with an empty package cache. CI performs the same checks on Windows and Ubuntu. See
[VERIFICATION.md](VERIFICATION.md), [RELEASE.md](RELEASE.md), and [provenance](docs/PROVENANCE.md).
The original `BenScr_Icon_Master.png` is embedded unchanged. The custom license is in
[LICENSE.txt](LICENSE.txt).
