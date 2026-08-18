namespace SafeSpeak.Core.Moderation;

public sealed class Blocklist
{
    private readonly HashSet<string> _terms;

    public Blocklist(IEnumerable<string>? terms = null)
    {
        _terms = new HashSet<string>(StringComparer.Ordinal);
        foreach (string term in terms ?? [])
        {
            Add(term);
        }
    }

    public IReadOnlyCollection<string> Terms => _terms;

    public bool Add(string term)
    {
        string normalized = TextNormalizer.Normalize(term).CompactComparisonText;
        return normalized.Length >= 3 && _terms.Add(normalized);
    }

    public bool Remove(string term)
    {
        string normalized = TextNormalizer.Normalize(term).CompactComparisonText;
        return _terms.Remove(normalized);
    }

    public bool Contains(TextNormalizationResult text) =>
        _terms.Any(term => text.CompactComparisonText.Contains(term, StringComparison.Ordinal));
}
