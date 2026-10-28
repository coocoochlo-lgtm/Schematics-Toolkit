using REDox;
using REDox.Json;
using System.Globalization;
using System.Text;

namespace SchematicsAgent;

/// <summary>
/// The assistant's API reference: every node, Behavior, Variable and Signal the toolkit documents with
/// [SchematicDoc]. Unity sends the full list after each recompile; anything new or changed is added here (and saved),
/// and each request pulls in the entries relevant to it, so a small model sees what exists and what it's for without
/// the whole reference in its context.
/// </summary>
public sealed class ReferenceStore
{
    public sealed class Entry
    {
        public string Key = "", Kind = "", Hash = "", Doc = "";
        public string Name = "", Card = "";
        public string[] Tokens = Array.Empty<string>();
        public string[] NameTokens = Array.Empty<string>();
    }

    private readonly string _path, _incoming;
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _documentFrequency = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    public int Count { get { lock (_gate) return _entries.Count; } }

    public ReferenceStore(AgentSettings settings)
    {
        string folder = settings.Reference;
        Directory.CreateDirectory(folder);
        _path = Path.Combine(folder, "api.json");
        _incoming = Path.Combine(folder, "incoming.json");
        if (File.Exists(_path))
            Merge(File.ReadAllText(_path), save: false, removeMissing: false);
        // What Unity left while the server wasn't running.
        if (File.Exists(_incoming))
        {
            var (added, updated, removed) = Merge(File.ReadAllText(_incoming), save: true, removeMissing: true);
            Console.WriteLine($"[reference] {Count} entries ({added} new, {updated} updated, {removed} removed)");
        }
        else
            Console.WriteLine($"[reference] {Count} entries");
    }

    /// <summary>
    /// Takes Unity's full list ([{key, kind, hash, doc}]): adds what's new, replaces what changed, and (for a full
    /// list) drops what no longer exists. Returns the counts.
    /// </summary>
    public (int added, int updated, int removed) Sync(string manifestJson)
    {
        var result = Merge(manifestJson, save: true, removeMissing: true);
        File.WriteAllText(_incoming, manifestJson);
        return result;
    }

    private (int added, int updated, int removed) Merge(string json, bool save, bool removeMissing)
    {
        int added = 0, updated = 0, removed = 0;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        using (var document = Json.Parse(json))
        {
            lock (_gate)
            {
                foreach (var item in document.RootElement.Items())
                {
                    string key = item.Str("key"), hash = item.Str("hash"), doc = item.Str("doc");
                    if (key.Length == 0 || doc.Length == 0)
                        continue;
                    seen.Add(key);
                    if (_entries.TryGetValue(key, out var existing) && existing.Hash == hash)
                        continue;
                    var entry = Build(key, item.Str("kind"), hash, doc);
                    if (entry == null)
                        continue;
                    if (existing == null) added++; else updated++;
                    _entries[key] = entry;
                }
                if (removeMissing)
                    foreach (var key in _entries.Keys.Where(k => !seen.Contains(k)).ToList())
                    {
                        _entries.Remove(key);
                        removed++;
                    }
                Reindex();
            }
        }
        if (save && (added + updated + removed) > 0)
            Save();
        return (added, updated, removed);
    }

    // One entry: its doc parsed for a compact card (what goes in prompts) and its search words.
    private static Entry? Build(string key, string kind, string hash, string doc)
    {
        try
        {
            using var parsed = Json.Parse(doc);
            var d = parsed.RootElement;
            string name = d.Str("name", key);
            kind = d.Str("kind", kind);
            var card = new StringBuilder();
            card.Append(kind).Append(' ').Append('"').Append(name).Append('"');
            string owner = d.Str("behavior");
            if (owner.Length > 0) card.Append(" of ").Append(owner);
            string flow = d.Str("flow"), type = d.Str("type"), direction = d.Str("direction");
            if (flow.Length > 0) card.Append(" (").Append(flow).Append(')');
            if (type.Length > 0) card.Append(" : ").Append(type);
            if (direction.Length > 0) card.Append(" (").Append(direction).Append(')');
            card.Append(" - ").Append(Shorten(d.Str("description"), 220));
            AppendPorts(card, " in", d.Child("inputs"));
            AppendPorts(card, " out", d.Child("outputs"));
            AppendPorts(card, " params", d.Child("params"));
            var uses = d.Child("use_for").Items().Select(u => u.GetString() ?? "").Where(u => u.Length > 0).ToList();
            if (uses.Count > 0) card.Append(" | for: ").Append(string.Join("; ", uses));
            string notes = d.Str("notes");
            if (notes.Length > 0) card.Append(" | ").Append(Shorten(notes, 160));

            string searchable = string.Join(' ', name, owner, d.Str("description"), string.Join(' ', uses), d.Str("category"), d.Str("menu"), kind);
            return new Entry
            {
                Key = key, Kind = kind, Hash = hash, Doc = doc, Name = name, Card = card.ToString(),
                Tokens = Tokenize(searchable).Distinct().ToArray(),
                NameTokens = Tokenize(name + " " + owner).Distinct().ToArray(),
            };
        }
        catch (Exception exception)
        {
            Console.WriteLine($"[reference] Skipping {key}: its JSON doesn't parse ({exception.Message})");
            return null;
        }
    }

    private static void AppendPorts(StringBuilder card, string label, DElement ports)
    {
        var list = ports.Items().Select(p => $"{p.Str("name")}:{p.Str("type")}").ToList();
        if (list.Count > 0) card.Append(label).Append('[').Append(string.Join(", ", list)).Append(']');
    }

    private static string Shorten(string text, int max) => text.Length <= max ? text : text.Substring(0, max - 1) + "…";

    private void Reindex()
    {
        _documentFrequency.Clear();
        foreach (var entry in _entries.Values)
            foreach (var token in entry.Tokens)
                _documentFrequency[token] = _documentFrequency.TryGetValue(token, out int n) ? n + 1 : 1;
    }

    private void Save()
    {
        var builder = new JsonBuilder();
        builder.BeginArray();
        lock (_gate)
            foreach (var entry in _entries.Values.OrderBy(e => e.Key, StringComparer.Ordinal))
                builder.BeginObject().String("key", entry.Key).String("kind", entry.Kind).String("hash", entry.Hash).String("doc", entry.Doc).EndObject();
        builder.EndArray();
        File.WriteAllText(_path, builder.ToString());
    }

    private static readonly HashSet<string> _stopWords = new(StringComparer.Ordinal)
    {
        "the", "a", "an", "and", "or", "of", "to", "in", "on", "for", "with", "it", "its", "is", "be", "this", "that",
        "when", "from", "by", "as", "at", "make", "add", "want", "should", "can", "i", "me", "my", "please", "so",
    };

    private static IEnumerable<string> Tokenize(string text)
    {
        foreach (var raw in System.Text.RegularExpressions.Regex.Split(SplitCamel(text).ToLowerInvariant(), "[^a-z0-9]+"))
        {
            if (raw.Length < 2 || _stopWords.Contains(raw)) continue;
            // A light stem, so "jumps"/"jumping" meet "jump".
            string token = raw.EndsWith("ing") && raw.Length > 5 ? raw[..^3] : raw.EndsWith("es") && raw.Length > 4 ? raw[..^2] : raw.EndsWith('s') && raw.Length > 3 ? raw[..^1] : raw;
            yield return token;
        }
    }

    private static string SplitCamel(string text) => System.Text.RegularExpressions.Regex.Replace(text ?? "", "(?<=[a-z0-9])(?=[A-Z])", " ");

    /// <summary>
    /// The entries most relevant to some text (a request, a plan, names on screen), best first, as prompt cards.
    /// Scored by shared words weighted by rarity, with name matches counting extra.
    /// </summary>
    public List<string> Search(string text, int count, ICollection<string>? alwaysInclude = null)
    {
        var query = Tokenize(text).ToHashSet(StringComparer.Ordinal);
        var scored = new List<(double score, Entry entry)>();
        lock (_gate)
        {
            int total = Math.Max(1, _entries.Count);
            foreach (var entry in _entries.Values)
            {
                double score = 0;
                foreach (var token in entry.Tokens)
                    if (query.Contains(token))
                        score += Math.Log(1.0 + total / (double)(_documentFrequency.TryGetValue(token, out int df) ? df : 1));
                foreach (var token in entry.NameTokens)
                    if (query.Contains(token))
                        score += 2.0;
                if (alwaysInclude != null && alwaysInclude.Contains(entry.Name))
                    score += 3.0;
                // Nodes and Behaviors are what plans are built from; their members follow.
                if (entry.Kind is "node" or "behavior" or "function") score *= 1.15;
                if (score > 0) scored.Add((score, entry));
            }
        }
        return scored.OrderByDescending(s => s.score).Take(count).Select(s => s.entry.Card).ToList();
    }

    public string Describe(string key)
    {
        lock (_gate) return _entries.TryGetValue(key, out var entry) ? entry.Card : "";
    }
}
