using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text;

namespace BenScr.TextMatching;

/// <summary>Ordinal Unicode-scalar edit distance and bounded, deterministically ranked fuzzy search.</summary>
/// <remarks>
/// Configuration is immutable and calls have independent state, so an instance can be shared between
/// threads. Each insertion, deletion or substitution costs one; transposition is not a single edit.
/// Comparison is case-sensitive, culture-independent and does not normalize text. Scalars are not
/// grapheme clusters. Ill-formed UTF-16 is rejected instead of silently replacing its code units.
/// </remarks>
public sealed class TextMatcher
{
    /// <summary>Creates a matcher with inclusive input and dynamic-programming limits.</summary>
    /// <param name="maxScalarLength">Maximum scalars per input, from one to 1,000,000.</param>
    /// <param name="maxCandidates">Maximum candidates per search, from one to 1,000,000.</param>
    /// <param name="maxCellUpdates">Maximum recurrence cells evaluated per public call, at least one. A search shares one budget across all candidates.</param>
    /// <exception cref="ArgumentOutOfRangeException">A setting is outside its allowed range.</exception>
    public TextMatcher(int maxScalarLength = 4096, int maxCandidates = 10_000, long maxCellUpdates = 10_000_000)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxScalarLength, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxScalarLength, 1_000_000);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxCandidates, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxCandidates, 1_000_000);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxCellUpdates, 1);
        MaxScalarLength = maxScalarLength;
        MaxCandidates = maxCandidates;
        MaxCellUpdates = maxCellUpdates;
    }

    /// <summary>Gets the inclusive Unicode-scalar limit for each input string.</summary>
    public int MaxScalarLength { get; }

    /// <summary>Gets the inclusive number of candidates in one search.</summary>
    public int MaxCandidates { get; }

    /// <summary>Gets the inclusive recurrence-cell budget per call; decoding, initialization and sorting are not cells.</summary>
    public long MaxCellUpdates { get; }

    /// <summary>Computes exact unit-cost Levenshtein distance between two well-formed strings.</summary>
    /// <param name="left">First string, including the empty string.</param>
    /// <param name="right">Second string, including the empty string.</param>
    /// <returns>The minimum number of scalar insertions, deletions and substitutions.</returns>
    /// <exception cref="ArgumentNullException">An input is null.</exception>
    /// <exception cref="ArgumentException">An input is ill-formed UTF-16 or exceeds <see cref="MaxScalarLength"/>.</exception>
    /// <exception cref="InvalidOperationException">The recurrence-cell budget is insufficient.</exception>
    public int Distance(string left, string right)
    {
        int[] first = Decode(left, nameof(left));
        int[] second = Decode(right, nameof(right));
        long remaining = MaxCellUpdates;
        return Compute(first, second, Math.Max(first.Length, second.Length), ref remaining)!.Value;
    }

    /// <summary>Returns the exact distance when it is at most a threshold, otherwise null.</summary>
    /// <param name="left">First well-formed string.</param>
    /// <param name="right">Second well-formed string.</param>
    /// <param name="maxDistance">Inclusive threshold from zero to <see cref="int.MaxValue"/>. Large values are safely capped by the input lengths.</param>
    /// <returns>An exact distance, including zero, or null when the threshold cannot be met.</returns>
    /// <exception cref="ArgumentNullException">An input is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxDistance"/> is negative.</exception>
    /// <exception cref="ArgumentException">An input is ill-formed UTF-16 or too long.</exception>
    /// <exception cref="InvalidOperationException">The recurrence-cell budget is insufficient.</exception>
    /// <remarks>Both strings are decoded and validated even if their lengths already rule out a match.</remarks>
    public int? DistanceWithin(string left, string right, int maxDistance)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxDistance);
        int[] first = Decode(left, nameof(left));
        int[] second = Decode(right, nameof(right));
        long remaining = MaxCellUpdates;
        return Compute(first, second, maxDistance, ref remaining);
    }

    /// <summary>Finds the closest candidates within an inclusive distance threshold.</summary>
    /// <param name="query">Well-formed query string, including empty.</param>
    /// <param name="candidates">Finite sequence of non-null, well-formed strings. Enumerated once and disposed. It must support the caller's concurrency requirements.</param>
    /// <param name="maxDistance">Inclusive distance threshold, at least zero.</param>
    /// <param name="maxResults">Maximum returned matches, at least one. Values larger than MaxCandidates are allowed; the input limit still bounds the actual result count.</param>
    /// <returns>A read-only, independently owned collection sorted by distance, then original input index. Duplicate candidate values remain distinct.</returns>
    /// <exception cref="ArgumentNullException">The query or sequence is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A threshold or result count is invalid.</exception>
    /// <exception cref="ArgumentException">A string is ill-formed or too long, or a candidate is null.</exception>
    /// <exception cref="InvalidOperationException">The candidate limit or cumulative recurrence-cell budget is exceeded.</exception>
    /// <remarks>
    /// Every candidate is validated even after enough results have been found. At most MaxCandidates
    /// plus one elements are requested, the extra element detecting an overlong sequence. Iterator
    /// exceptions propagate unchanged; no partial results are returned. Caller-owned iterators can
    /// block, so the configured limits are resource bounds, not a timeout. No asynchronous work runs.
    /// </remarks>
    public IReadOnlyList<TextMatch> FindClosest(string query, IEnumerable<string> candidates, int maxDistance, int maxResults = 10)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentOutOfRangeException.ThrowIfNegative(maxDistance);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxResults, 1);
        int[] queryScalars = Decode(query, nameof(query));
        var matches = new PriorityQueue<TextMatch, (int Distance, int Index)>();
        long remaining = MaxCellUpdates;
        int index = 0;
        foreach (string candidate in candidates)
        {
            if (index == MaxCandidates)
            {
                throw new InvalidOperationException("Candidate limit exceeded.");
            }
            if (candidate is null)
            {
                throw new ArgumentException("Candidates must not contain null.", nameof(candidates));
            }
            int[] scalars = Decode(candidate, nameof(candidates));
            int? distance = Compute(queryScalars, scalars, maxDistance, ref remaining);
            if (distance.HasValue && (matches.Count < maxResults || distance.Value < matches.Peek().Distance))
            {
                if (matches.Count == maxResults)
                {
                    matches.Dequeue();
                }
                matches.Enqueue(new TextMatch(candidate, index, distance.Value), (-distance.Value, -index));
            }
            index++;
        }
        var result = new TextMatch[matches.Count];
        // The heap removes the worst match first, so fill the result backward.
        for (int resultIndex = result.Length - 1; resultIndex >= 0; resultIndex--)
        {
            result[resultIndex] = matches.Dequeue();
        }
        return Array.AsReadOnly(result);
    }

    private int[] Decode(string text, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(text, parameterName);
        if (text.Length > MaxScalarLength * 2)
        {
            throw new ArgumentException("Scalar length limit exceeded.", parameterName);
        }
        int[] scalars = new int[Math.Min(text.Length, MaxScalarLength)];
        int count = 0;
        ReadOnlySpan<char> input = text.AsSpan();
        while (!input.IsEmpty)
        {
            if (count == MaxScalarLength)
            {
                throw new ArgumentException("Scalar length limit exceeded.", parameterName);
            }
            if (Rune.DecodeFromUtf16(input, out Rune scalar, out int consumed) != OperationStatus.Done)
            {
                throw new ArgumentException("Input contains ill-formed UTF-16.", parameterName);
            }
            scalars[count++] = scalar.Value;
            input = input[consumed..];
        }
        if (count != scalars.Length)
        {
            Array.Resize(ref scalars, count);
        }
        return scalars;
    }

    private static int? Compute(int[] first, int[] second, int threshold, ref long remaining)
    {
        // Keep the shorter dimension in the row buffers.
        if (first.Length < second.Length)
        {
            (first, second) = (second, first);
        }
        int rows = first.Length;
        int columns = second.Length;
        int limit = Math.Min(threshold, rows);
        if (rows - columns > limit)
        {
            return null;
        }
        if (columns == 0)
        {
            return rows;
        }
        int infinity = limit + 1;
        var previous = new int[columns + 1];
        var current = new int[columns + 1];
        for (int column = 0; column <= columns; column++)
        {
            previous[column] = column <= limit ? column : infinity;
        }
        for (int row = 1; row <= rows; row++)
        {
            int start = Math.Max(1, row - limit);
            int end = Math.Min(columns, row + limit);
            int updates = end - start + 1;
            if (remaining < updates)
            {
                throw new InvalidOperationException("Recurrence-cell budget exceeded.");
            }
            remaining -= updates;
            current[0] = row <= limit ? row : infinity;
            if (start > 1)
            {
                current[start - 1] = infinity;
            }
            int rowMinimum = current[0];
            for (int column = start; column <= end; column++)
            {
                int substitution = previous[column - 1] + (first[row - 1] == second[column - 1] ? 0 : 1);
                int insertion = current[column - 1] + 1;
                int deletion = previous[column] + 1;
                current[column] = Math.Min(infinity, Math.Min(substitution, Math.Min(insertion, deletion)));
                rowMinimum = Math.Min(rowMinimum, current[column]);
            }
            if (rowMinimum > limit)
            {
                return null;
            }
            if (end < columns)
            {
                current[end + 1] = infinity;
            }
            (previous, current) = (current, previous);
        }
        return previous[columns] <= limit ? previous[columns] : null;
    }
}
