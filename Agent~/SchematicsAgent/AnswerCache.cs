using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SchematicsAgent;

/// <summary>
/// Answers Sprocket has already given, kept on disk (answers.json next to the models) so asking again is instant.
/// Only for chat and explanations: answers about what's open depend on the editor, which changes all the time.
/// <para>
/// An entry remembers what it was based on (a fingerprint of the reference entries it read), and only counts while
/// that's unchanged: when the toolkit's docs change, answers built on them are made fresh. Questions match when
/// they're worded nearly the same ("what is a signal" / "what's a Signal?").
/// </para>
/// </summary>
public sealed class AnswerCache
{
    private sealed class Entry
    {
        public string Mode { get; set; } = "";
        public string Model { get; set; } = "";
        public string Question { get; set; } = "";
        public string[] Words { get; set; } = Array.Empty<string>();
        public string Basis { get; set; } = "";
        public List<string> Answers { get; set; } = new();
        public DateTime Used { get; set; }
    }

    // A chat reply is kept in a few versions, so "hi" isn't always answered the same way.
    private const int ChatVariants = 3;
    private const int MaxEntries = 500;
    private const double SameQuestion = 0.8;

    private readonly string _path;
    private readonly object _gate = new();
    private readonly List<Entry> _entries;
    private readonly Random _random = new();

    public AnswerCache(AgentSettings settings)
    {
        _path = Path.Combine(settings.AgentFolder, "answers.json");
        try { _entries = File.Exists(_path) ? JsonSerializer.Deserialize<List<Entry>>(File.ReadAllText(_path)) ?? new() : new(); }
        catch { _entries = new(); }
    }

    /// <summary>A fingerprint of what an answer is based on (the reference text it read), to tell when it's stale.</summary>
    public static string Fingerprint(string basis)
        => basis.Length == 0 ? "" : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(basis)))[..16];

    /// <summary>
    /// A remembered answer to this question (or one worded nearly the same), given with the same model and basis.
    /// Null when there isn't one, or when a chat reply should get a new version.
    /// </summary>
    public string? Find(Mode mode, string model, string question, string basis)
    {
        var words = Words(question);
        lock (_gate)
        {
            var entry = Best(mode, model, words, basis);
            if (entry == null || entry.Answers.Count == 0)
                return null;
            // Chat: make new versions until there are a few, then pick among them.
            if (mode == SchematicsAgent.Mode.Chat && entry.Answers.Count < ChatVariants)
                return null;
            entry.Used = DateTime.UtcNow;
            return entry.Answers[_random.Next(entry.Answers.Count)];
        }
    }

    public void Remember(Mode mode, string model, string question, string basis, string answer)
    {
        if (string.IsNullOrWhiteSpace(answer))
            return;
        var words = Words(question);
        lock (_gate)
        {
            var entry = Best(mode, model, words, basis);
            if (entry == null)
            {
                entry = new Entry { Mode = mode.ToString(), Model = model, Question = question, Words = words, Basis = basis };
                _entries.Add(entry);
            }
            if (!entry.Answers.Contains(answer))
            {
                entry.Answers.Add(answer);
                if (entry.Answers.Count > ChatVariants)
                    entry.Answers.RemoveAt(0);
            }
            entry.Used = DateTime.UtcNow;
            // Stale entries (same question, older basis) and the least used go.
            _entries.RemoveAll(e => e != entry && e.Mode == entry.Mode && e.Model == model && e.Basis != basis && Similarity(e.Words, words) >= SameQuestion);
            if (_entries.Count > MaxEntries)
                _entries.Remove(_entries.MinBy(e => e.Used)!);
            Save();
        }
    }

    /// <summary>What <see cref="Fuse"/> found: the answer (null if the memories don't cover the question), and how much they covered.</summary>
    public readonly record struct Fusion(string? Answer, int Sources, double Coverage);

    // Fusing is only trusted when the memories cover most of what the question is about.
    private const double Covered = 0.75;
    private const int MaxSources = 3, SentencesPerSource = 2;

    /// <summary>
    /// A "pseudo-memory": an answer to a new question put together from the sentences of earlier explanations that
    /// cover it, without the model. "What's a Signal, and how do Variables work?" can be built from separate answers
    /// about Signals and Variables. <paramref name="stillValid"/> says whether a memory's basis still holds (its
    /// reference unchanged); stale memories aren't used. Fused answers are never remembered themselves, so they
    /// can't be fused again (a copy of a copy).
    /// </summary>
    public Fusion Fuse(string model, string question, Func<string, string, bool> stillValid)
    {
        var wanted = Words(question).ToHashSet(StringComparer.Ordinal);
        if (wanted.Count == 0)
            return new Fusion(null, 0, 0);

        List<Entry> candidates;
        lock (_gate)
            candidates = _entries.Where(e => e.Mode == SchematicsAgent.Mode.Explain.ToString() && e.Model == model && e.Answers.Count > 0
                                             && e.Words.Any(wanted.Contains)).ToList();

        // Greedy cover: each next memory is the one that covers the most words not covered yet.
        var uncovered = new HashSet<string>(wanted, StringComparer.Ordinal);
        var chosen = new List<(Entry entry, HashSet<string> covers)>();
        while (chosen.Count < MaxSources && uncovered.Count > 0)
        {
            var best = candidates
                .Where(e => !chosen.Any(c => c.entry == e))
                .Select(e => (entry: e, covers: e.Words.Where(uncovered.Contains).ToHashSet(StringComparer.Ordinal)))
                .Where(x => x.covers.Count > 0)
                .OrderByDescending(x => x.covers.Count).ThenByDescending(x => x.entry.Used)
                .FirstOrDefault();
            if (best.entry == null)
                break;
            candidates.Remove(best.entry);
            if (!stillValid(best.entry.Question, best.entry.Basis))
                continue;   // its reference changed since: don't build on it
            chosen.Add(best);
            uncovered.ExceptWith(best.covers);
        }

        double coverage = 1.0 - (double)uncovered.Count / wanted.Count;
        if (chosen.Count == 0 || coverage < Covered)
            return new Fusion(null, chosen.Count, coverage);
        if (chosen.Count > 1 && (Relating.IsMatch(question) || !SeveralAsks.IsMatch(question)))
            return new Fusion(null, chosen.Count, coverage);

        // In the order the question asks about them.
        string lowered = question.ToLowerInvariant();
        chosen = chosen.OrderBy(c => c.covers.Select(w => lowered.IndexOf(w, StringComparison.Ordinal)).Where(i => i >= 0).DefaultIfEmpty(int.MaxValue).Min()).ToList();

        // From each memory, its sentences most about the question, in their own order; repeats dropped.
        var sentences = new List<string>();
        var said = new List<string[]>();
        foreach (var (entry, covers) in chosen)
        {
            var parts = Regex.Split(entry.Answers[0].Trim(), @"(?<=[.!?])\s+").Where(p => p.Trim().Length > 0).ToList();
            var keep = parts
                .Select((text, index) => (text, index, words: Words(text), score: Words(text).Count(w => wanted.Contains(w)) + (index == 0 ? 0.5 : 0)))
                .OrderByDescending(p => p.score).Take(SentencesPerSource)
                .Where(p => !said.Any(s => Similarity(s, p.words) >= 0.7))
                .OrderBy(p => p.index);
            foreach (var part in keep)
            {
                sentences.Add(part.text.Trim());
                said.Add(part.words);
            }
        }
        lock (_gate)
            foreach (var (entry, _) in chosen)
                entry.Used = DateTime.UtcNow;
        return sentences.Count == 0 ? new Fusion(null, chosen.Count, coverage) : new Fusion(string.Join(" ", sentences), chosen.Count, coverage);
    }

    public void Clear()
    {
        lock (_gate)
        {
            _entries.Clear();
            Save();
        }
    }

    private Entry? Best(Mode mode, string model, string[] words, string basis)
    {
        Entry? best = null;
        double bestScore = SameQuestion;
        foreach (var entry in _entries)
        {
            if (entry.Mode != mode.ToString() || entry.Model != model || entry.Basis != basis)
                continue;
            double score = Similarity(entry.Words, words);
            if (score >= bestScore)
            {
                best = entry;
                bestScore = score;
            }
        }
        return best;
    }

    private void Save()
    {
        try { File.WriteAllText(_path, JsonSerializer.Serialize(_entries)); }
        catch (Exception exception) { Console.Error.WriteLine($"[cache] Couldn't save answers.json: {exception.Message}"); }
    }

    // Words that change the meaning; "a", "the", "please" and the like don't.
    private static readonly HashSet<string> Filler = new(StringComparer.Ordinal)
    {
        "a", "an", "the", "is", "are", "was", "be", "do", "does", "to", "of", "in", "on", "for", "and", "or", "it", "its",
        "me", "you", "i", "my", "your", "please", "can", "could", "would", "tell", "about", "sprocket", "hey", "so", "um", "uh", "just",
        // Question words say how it's asked, not what about.
        "what", "how", "why", "which", "who", "also", "work", "works", "mean", "means", "explain", "thing", "use", "used", "using",
    };

    // A question about how things relate ("how do Signals work with Variables?") isn't answered by putting separate
    // answers side by side; only one that asks several things ("what's a Signal, and what's a Variable?") is.
    private static readonly Regex Relating = new(
        @"\b(with|together|between|differen\w*|vs\.?|versus|compare\w*|relat\w*|instead|better|same|connect\w*|inside|into|from)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex SeveralAsks = new(@"\band\b|,|;|\balso\b|\?.+", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static string[] Words(string text)
    {
        // "what's" -> "what", "signals" -> "signal": close enough for matching.
        var words = Regex.Split(text.ToLowerInvariant().Replace("'s", "").Replace("’s", ""), "[^a-z0-9]+")
            .Where(w => w.Length > 0 && !Filler.Contains(w))
            .Select(w => w.Length > 3 && w.EndsWith('s') && !w.EndsWith("ss") ? w[..^1] : w)
            .Distinct().OrderBy(w => w, StringComparer.Ordinal).ToArray();
        return words.Length > 0 ? words : new[] { text.Trim().ToLowerInvariant() };
    }

    private static double Similarity(string[] a, string[] b)
    {
        if (a.Length == 0 || b.Length == 0) return 0;
        int shared = a.Intersect(b, StringComparer.Ordinal).Count();
        return (double)shared / (a.Length + b.Length - shared);
    }
}
