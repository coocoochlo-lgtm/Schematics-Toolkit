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
    };

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
