namespace BenScr.TextMatching;

/// <summary>An immutable fuzzy-search result referring to the original candidate string.</summary>
public sealed class TextMatch
{
    internal TextMatch(string candidate, int index, int distance)
    {
        Candidate = candidate;
        Index = index;
        Distance = distance;
    }

    /// <summary>Gets the original, unmodified candidate string.</summary>
    public string Candidate { get; }

    /// <summary>Gets the candidate's zero-based position in the input sequence.</summary>
    public int Index { get; }

    /// <summary>Gets the exact unit-cost Levenshtein distance in Unicode scalar values.</summary>
    public int Distance { get; }
}
