// The editor token language ("macros") a small model is trained to write: shared word for word by the Unity
// editor (which builds the vocabulary and the training data) and Sprocket's server (which runs the trained model
// and turns what it writes back into editor commands). Plain C# with no Unity or server dependencies, so both
// compile this same file and can never disagree about the format.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Remedy.Schematics.Assistant.Macro
{
    #region Vocabulary

    /// <summary>One token: a fixed word of the language, or a node type / Behavior from some assembly.</summary>
    public sealed class MacroToken
    {
        /// <summary>The token's text, e.g. "&lt;connect&gt;" or "&lt;Remedy.Schematics/node/OnUpdateNode&gt;".</summary>
        public string Text = "";
        /// <summary>core, ref, new, node or behavior.</summary>
        public string Kind = "";
        /// <summary>The assembly that declares it (its namespace in the vocabulary), or "core".</summary>
        public string Assembly = "core";
        /// <summary>The C# type name (node and behavior tokens).</summary>
        public string TypeName = "";
        /// <summary>What people call it ("On Update").</summary>
        public string Name = "";
        /// <summary>Words that describe it: a new token's embedding starts as the average of these words' tokens.</summary>
        public string Description = "";
    }

    /// <summary>
    /// The tokens: the fixed words, numbered references, and one token per node type and Behavior, each namespaced
    /// by its assembly so two modules can both have a "Grab" without clashing. Saved as a tab-separated file.
    /// </summary>
    public sealed class MacroVocabulary
    {
        public const int MaxRefs = 64, MaxNew = 16;

        public readonly List<MacroToken> Tokens = new List<MacroToken>();
        private readonly Dictionary<string, MacroToken> _byText = new Dictionary<string, MacroToken>(StringComparer.Ordinal);
        private readonly Dictionary<string, MacroToken> _nodesByName = new Dictionary<string, MacroToken>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, MacroToken> _behaviorsByName = new Dictionary<string, MacroToken>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The fixed words, in order (state markers, commands, modes, ports).</summary>
        public static readonly string[] CoreWords =
        {
            "<state>", "</state>", "<node>", "<in>", "<out>", "<link>", "<object>", "<behavior>", "<var>", "<signal>",
            "<request>", "<macro>", "<end>",
            "<add>", "<add_signal>", "<add_variable>", "<add_behavior_ref>", "<add_behavior>", "<connect>", "<set>",
            "<delete>", "<focus>",
            "<mode:send>", "<mode:receive>", "<mode:get>", "<mode:set>", "<dir:in>", "<dir:out>", "<flow>",
        };

        public static string Ref(int index) => "<ref:" + index.ToString(CultureInfo.InvariantCulture) + ">";
        public static string New(int index) => "<new:" + index.ToString(CultureInfo.InvariantCulture) + ">";
        public static string NodeToken(string assembly, string typeName) => "<" + assembly + "/node/" + typeName + ">";
        public static string BehaviorToken(string assembly, string typeName) => "<" + assembly + "/behavior/" + typeName + ">";

        /// <summary>A vocabulary with just the fixed words and references; node types and Behaviors are added to it.</summary>
        public static MacroVocabulary Core()
        {
            var vocabulary = new MacroVocabulary();
            foreach (var word in CoreWords)
                vocabulary.Add(new MacroToken { Text = word, Kind = "core", Name = word.Trim('<', '>'), Description = Describe(word) });
            for (int i = 1; i <= MaxRefs; i++)
                vocabulary.Add(new MacroToken { Text = Ref(i), Kind = "ref", Name = "ref " + i, Description = "reference " + i });
            for (int i = 1; i <= MaxNew; i++)
                vocabulary.Add(new MacroToken { Text = New(i), Kind = "new", Name = "new " + i, Description = "new node " + i });
            return vocabulary;
        }

        private static string Describe(string word)
        {
            string bare = word.Trim('<', '>', '/').Replace("_", " ").Replace(":", " ");
            return word.StartsWith("</", StringComparison.Ordinal) ? "end of " + bare : bare;
        }

        public void Add(MacroToken token)
        {
            if (token == null || string.IsNullOrEmpty(token.Text) || _byText.ContainsKey(token.Text))
                return;
            Tokens.Add(token);
            _byText[token.Text] = token;
            var names = token.Kind == "node" ? _nodesByName : token.Kind == "behavior" ? _behaviorsByName : null;
            if (names != null)
            {
                if (!string.IsNullOrEmpty(token.Name) && !names.ContainsKey(token.Name)) names[token.Name] = token;
                if (!string.IsNullOrEmpty(token.TypeName) && !names.ContainsKey(token.TypeName)) names[token.TypeName] = token;
            }
        }

        public MacroToken Find(string text) => text != null && _byText.TryGetValue(text, out var token) ? token : null;

        /// <summary>A node type's token by its display or type name.</summary>
        public MacroToken Node(string name) => name != null && _nodesByName.TryGetValue(name, out var token) ? token : null;

        /// <summary>A Behavior's token by its display or type name.</summary>
        public MacroToken Behavior(string name) => name != null && _behaviorsByName.TryGetValue(name, out var token) ? token : null;

        /// <summary>A short fingerprint of the vocabulary: a model only works with the vocabulary it was trained on.</summary>
        public string Fingerprint()
        {
            unchecked
            {
                ulong hash = 1469598103934665603UL;
                foreach (var token in Tokens)
                    foreach (char c in token.Text)
                        hash = (hash ^ c) * 1099511628211UL;
                return hash.ToString("x16", CultureInfo.InvariantCulture);
            }
        }

        public void Save(string path)
        {
            var text = new StringBuilder("# token\tkind\tassembly\ttype\tname\tdescription\n");
            foreach (var token in Tokens)
                text.Append(Clean(token.Text)).Append('\t').Append(token.Kind).Append('\t').Append(Clean(token.Assembly)).Append('\t')
                    .Append(Clean(token.TypeName)).Append('\t').Append(Clean(token.Name)).Append('\t').Append(Clean(token.Description)).Append('\n');
            File.WriteAllText(path, text.ToString(), new UTF8Encoding(false));
        }

        public static MacroVocabulary Load(string path)
        {
            var vocabulary = new MacroVocabulary();
            foreach (var line in File.ReadAllLines(path))
            {
                if (line.Length == 0 || line[0] == '#')
                    continue;
                var parts = line.Split('\t');
                if (parts.Length < 6)
                    continue;
                vocabulary.Add(new MacroToken { Text = parts[0], Kind = parts[1], Assembly = parts[2], TypeName = parts[3], Name = parts[4], Description = parts[5] });
            }
            return vocabulary;
        }

        private static string Clean(string text) => (text ?? "").Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' ');
    }

    #endregion

    #region Editor state and commands

    /// <summary>A node in the open graph, as the model sees it.</summary>
    public sealed class StateNode
    {
        /// <summary>The id the editor uses for it ("n3").</summary>
        public string Id = "";
        /// <summary>Its node type's display or type name.</summary>
        public string Type = "";
        public string Name = "";
        public List<string> Inputs = new List<string>();
        public List<string> Outputs = new List<string>();
        public bool HasFlowIn, HasFlowOut;
        /// <summary>Links out of it: (from port, to node id, to port); "flow" for the flow arrow.</summary>
        public List<(string fromPort, string to, string toPort)> Links = new List<(string, string, string)>();
    }

    public sealed class StateBehavior
    {
        public string Type = "";
        public List<(string name, string type)> Variables = new List<(string, string)>();
        /// <summary>Signals and whether the graph sends them (in) or receives them (out).</summary>
        public List<(string name, bool input)> Signals = new List<(string, bool)>();
    }

    public sealed class StateObject
    {
        /// <summary>Path from the prefab root ("" for the root).</summary>
        public string Path = "";
        public string Name = "";
        public List<StateBehavior> Behaviors = new List<StateBehavior>();
    }

    /// <summary>What's open in the editor: the graph's nodes and the prefab's objects with their Behaviors.</summary>
    public sealed class MacroState
    {
        public List<StateNode> Nodes = new List<StateNode>();
        public List<StateObject> Objects = new List<StateObject>();
    }

    /// <summary>One editor command, in the same shape as Sprocket's JSON commands.</summary>
    public sealed class MacroCommand
    {
        public string Op = "", Id = "", Type = "", Node = "", From = "", FromPort = "", To = "", ToPort = "";
        public string Behavior = "", Signal = "", Variable = "", Mode = "", Path = "", Field = "", Value = "";

        public override string ToString() => Op + " " + string.Join(" ", new[] { Id, Type, Node, From, FromPort, To, ToPort, Behavior, Signal, Variable, Mode, Path, Field, Value }.Where(s => !string.IsNullOrEmpty(s)));
    }

    #endregion

    /// <summary>
    /// Writes the editor state and a request as the model's prompt, encodes commands as its answer, and decodes what
    /// it writes back into commands. References: the state's nodes, objects and Behaviors are numbered in order
    /// (&lt;ref:1&gt;, ...); nodes the answer makes are &lt;new:1&gt;, ... Names people chose (Variables, Signals,
    /// ports, values) stay as quoted text the model copies from the state.
    /// </summary>
    public sealed class MacroLanguage
    {
        public readonly MacroVocabulary Vocabulary;

        public MacroLanguage(MacroVocabulary vocabulary) => Vocabulary = vocabulary;

        #region References

        /// <summary>What each reference points at in a state.</summary>
        public sealed class References
        {
            public readonly Dictionary<string, string> NodeRef = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);   // node id -> ref
            public readonly Dictionary<string, StateNode> Nodes = new Dictionary<string, StateNode>(StringComparer.Ordinal);       // ref -> node
            public readonly Dictionary<string, StateObject> Objects = new Dictionary<string, StateObject>(StringComparer.Ordinal); // ref -> object
            public readonly Dictionary<string, (StateObject owner, StateBehavior behavior)> Behaviors = new Dictionary<string, (StateObject, StateBehavior)>(StringComparer.Ordinal);
            public readonly Dictionary<StateBehavior, string> BehaviorRef = new Dictionary<StateBehavior, string>();
            public readonly Dictionary<StateObject, string> ObjectRef = new Dictionary<StateObject, string>();
        }

        public static References Number(MacroState state)
        {
            var refs = new References();
            int next = 1;
            foreach (var node in state.Nodes)
            {
                if (next > MacroVocabulary.MaxRefs) break;
                string r = MacroVocabulary.Ref(next++);
                refs.NodeRef[node.Id] = r;
                refs.Nodes[r] = node;
            }
            foreach (var obj in state.Objects)
            {
                if (next > MacroVocabulary.MaxRefs) break;
                string r = MacroVocabulary.Ref(next++);
                refs.Objects[r] = obj;
                refs.ObjectRef[obj] = r;
                foreach (var behavior in obj.Behaviors)
                {
                    if (next > MacroVocabulary.MaxRefs) break;
                    string b = MacroVocabulary.Ref(next++);
                    refs.Behaviors[b] = (obj, behavior);
                    refs.BehaviorRef[behavior] = b;
                }
            }
            return refs;
        }

        #endregion

        #region Prompt

        /// <summary>The prompt: the state, the request, then where the answer starts.</summary>
        public string Prompt(MacroState state, string request)
        {
            var refs = Number(state);
            var text = new StringBuilder("<state>\n");
            foreach (var node in state.Nodes)
            {
                if (!refs.NodeRef.TryGetValue(node.Id, out var r)) continue;
                text.Append("<node> ").Append(r).Append(' ').Append(NodeWord(node.Type));
                if (!string.IsNullOrEmpty(node.Name) && !Same(node.Name, Vocabulary.Node(node.Type)?.Name ?? node.Type))
                    text.Append(' ').Append(Quote(node.Name));
                if (node.HasFlowIn || node.Inputs.Count > 0)
                {
                    text.Append(" <in>");
                    if (node.HasFlowIn) text.Append(" <flow>");
                    foreach (var port in node.Inputs) text.Append(' ').Append(Quote(port));
                }
                if (node.HasFlowOut || node.Outputs.Count > 0)
                {
                    text.Append(" <out>");
                    if (node.HasFlowOut) text.Append(" <flow>");
                    foreach (var port in node.Outputs) text.Append(' ').Append(Quote(port));
                }
                text.Append('\n');
            }
            foreach (var node in state.Nodes)
                foreach (var (fromPort, to, toPort) in node.Links)
                    if (refs.NodeRef.TryGetValue(node.Id, out var from) && refs.NodeRef.TryGetValue(to, out var target))
                        text.Append("<link> ").Append(from).Append(' ').Append(Port(fromPort)).Append(' ').Append(target).Append(' ').Append(Port(toPort)).Append('\n');
            foreach (var obj in state.Objects)
            {
                if (!refs.ObjectRef.TryGetValue(obj, out var o)) continue;
                text.Append("<object> ").Append(o).Append(' ').Append(Quote(obj.Name));
                if (!string.IsNullOrEmpty(obj.Path)) text.Append(' ').Append(Quote(obj.Path));
                text.Append('\n');
                foreach (var behavior in obj.Behaviors)
                {
                    if (!refs.BehaviorRef.TryGetValue(behavior, out var b)) continue;
                    text.Append("<behavior> ").Append(b).Append(' ').Append(BehaviorWord(behavior.Type));
                    foreach (var (name, type) in behavior.Variables)
                        text.Append(" <var> ").Append(Quote(name)).Append(' ').Append(Quote(type));
                    foreach (var (name, input) in behavior.Signals)
                        text.Append(" <signal> ").Append(Quote(name)).Append(input ? " <dir:in>" : " <dir:out>");
                    text.Append('\n');
                }
            }
            text.Append("</state>\n<request> ").Append(OneLine(request)).Append("\n<macro>");
            return text.ToString();
        }

        private string NodeWord(string type) => Vocabulary.Node(type)?.Text ?? Quote(type);
        private string BehaviorWord(string type) => Vocabulary.Behavior(type)?.Text ?? Quote(type);
        private static string Port(string port) => string.Equals(port, "flow", StringComparison.OrdinalIgnoreCase) ? "<flow>" : Quote(port);

        #endregion

        #region Encoding (commands -> answer)

        /// <summary>
        /// Commands as the answer the model should write. Null when something can't be expressed in the language
        /// (a node type or Behavior outside the vocabulary, a reference to something not in the state).
        /// </summary>
        public string Encode(MacroState state, IEnumerable<MacroCommand> commands)
        {
            var refs = Number(state);
            var made = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var text = new StringBuilder();
            string Handle(string id)
            {
                if (string.IsNullOrEmpty(id)) return null;
                if (made.TryGetValue(id, out var n)) return n;
                return refs.NodeRef.TryGetValue(id, out var r) ? r : null;
            }
            string BehaviorRef(string type, string path)
            {
                foreach (var pair in refs.Behaviors)
                    if (Same(pair.Value.behavior.Type, type) && (path == null || Same(pair.Value.owner.Path, path)))
                        return pair.Key;
                return null;
            }
            foreach (var c in commands)
            {
                text.Append(' ');
                switch (c.Op)
                {
                    case "add_node":
                    {
                        var token = Vocabulary.Node(c.Type);
                        if (token == null || made.Count >= MacroVocabulary.MaxNew) return null;
                        string n = MacroVocabulary.New(made.Count + 1);
                        made[c.Id] = n;
                        text.Append("<add> ").Append(n).Append(' ').Append(token.Text);
                        break;
                    }
                    case "add_signal_node":
                    case "add_variable_node":
                    case "add_behavior_node":
                    {
                        string b = BehaviorRef(c.Behavior, c.Path);
                        if (b == null || made.Count >= MacroVocabulary.MaxNew) return null;
                        string n = MacroVocabulary.New(made.Count + 1);
                        made[c.Id] = n;
                        if (c.Op == "add_signal_node")
                            text.Append("<add_signal> ").Append(n).Append(' ').Append(b).Append(' ').Append(Quote(c.Signal)).Append(c.Mode == "send" ? " <mode:send>" : " <mode:receive>");
                        else if (c.Op == "add_variable_node")
                            text.Append("<add_variable> ").Append(n).Append(' ').Append(b).Append(' ').Append(Quote(c.Variable)).Append(c.Mode == "set" ? " <mode:set>" : " <mode:get>");
                        else
                            text.Append("<add_behavior_ref> ").Append(n).Append(' ').Append(b);
                        break;
                    }
                    case "add_behavior":
                    {
                        var token = Vocabulary.Behavior(c.Behavior);
                        var obj = refs.Objects.FirstOrDefault(pair => Same(pair.Value.Path, c.Path ?? ""));
                        if (token == null || obj.Key == null) return null;
                        text.Append("<add_behavior> ").Append(token.Text).Append(' ').Append(obj.Key);
                        break;
                    }
                    case "connect":
                    {
                        string from = Handle(c.From), to = Handle(c.To);
                        if (from == null || to == null) return null;
                        text.Append("<connect> ").Append(from).Append(' ').Append(Port(c.FromPort)).Append(' ').Append(to).Append(' ').Append(Port(c.ToPort));
                        break;
                    }
                    case "set_value":
                    {
                        string node = Handle(c.Node);
                        if (node == null) return null;
                        text.Append("<set> ").Append(node).Append(' ').Append(Quote(c.Field)).Append(' ').Append(Quote(c.Value));
                        break;
                    }
                    case "delete_node":
                    case "focus":
                    {
                        string node = Handle(c.Node);
                        if (node == null) return null;
                        text.Append(c.Op == "delete_node" ? "<delete> " : "<focus> ").Append(node);
                        break;
                    }
                    default:
                        return null;
                }
                text.Append('\n');
            }
            text.Append(" <end>");
            return text.ToString();
        }

        #endregion

        #region Decoding (answer -> commands)

        private static readonly Regex Piece = new Regex("<[^<>\\s]+>|\"(?:[^\"\\\\]|\\\\.)*\"", RegexOptions.Compiled);

        /// <summary>
        /// Turns what the model wrote back into commands, against the state it was given. Problems (an unknown
        /// reference, a missing name) are listed; commands decoded before a problem are kept.
        /// </summary>
        public List<MacroCommand> Decode(MacroState state, string answer, out List<string> problems)
        {
            problems = new List<string>();
            var refs = Number(state);
            var commands = new List<MacroCommand>();
            var pieces = Piece.Matches(answer ?? "").Cast<Match>().Select(m => m.Value).ToList();
            int i = 0;
            string Next() => i < pieces.Count ? pieces[i++] : null;
            string Peek() => i < pieces.Count ? pieces[i] : null;
            var made = new Dictionary<string, string>(StringComparer.Ordinal);   // <new:k> -> id

            string NodeId(string piece)
            {
                if (piece == null) return null;
                if (made.TryGetValue(piece, out var id)) return id;
                return refs.Nodes.TryGetValue(piece, out var node) ? node.Id : null;
            }
            string Text(string piece) => piece != null && piece.Length >= 2 && piece[0] == '"' ? Unquote(piece) : null;
            string PortText(string piece) => piece == "<flow>" ? "flow" : Text(piece);

            while (i < pieces.Count)
            {
                string word = Next();
                if (word == "<end>") break;
                var c = new MacroCommand();
                string problem = null;
                switch (word)
                {
                    case "<add>":
                    {
                        string n = Next();
                        var token = Vocabulary.Find(Next());
                        if (n == null || !n.StartsWith("<new:", StringComparison.Ordinal) || token == null || token.Kind != "node") { problem = "an <add> without a new node and a node type"; break; }
                        c.Op = "add_node"; c.Id = "new" + n.Substring(5, n.Length - 6); c.Type = token.Name;
                        made[n] = c.Id;
                        break;
                    }
                    case "<add_signal>":
                    case "<add_variable>":
                    case "<add_behavior_ref>":
                    {
                        string n = Next(), b = Next();
                        if (n == null || !n.StartsWith("<new:", StringComparison.Ordinal) || b == null || !refs.Behaviors.TryGetValue(b, out var owner)) { problem = word + " without a new node and a Behavior reference"; break; }
                        c.Id = "new" + n.Substring(5, n.Length - 6); c.Behavior = owner.behavior.Type; c.Path = owner.owner.Path;
                        made[n] = c.Id;
                        if (word == "<add_behavior_ref>") { c.Op = "add_behavior_node"; break; }
                        string name = Text(Next()), mode = Next();
                        if (name == null) { problem = word + " without a name"; break; }
                        if (word == "<add_signal>") { c.Op = "add_signal_node"; c.Signal = name; c.Mode = mode == "<mode:send>" ? "send" : "receive"; }
                        else { c.Op = "add_variable_node"; c.Variable = name; c.Mode = mode == "<mode:set>" ? "set" : "get"; }
                        break;
                    }
                    case "<add_behavior>":
                    {
                        var token = Vocabulary.Find(Next());
                        string o = Next();
                        if (token == null || token.Kind != "behavior" || o == null || !refs.Objects.TryGetValue(o, out var obj)) { problem = "an <add_behavior> without a Behavior and an object"; break; }
                        c.Op = "add_behavior"; c.Behavior = token.TypeName; c.Path = obj.Path;
                        break;
                    }
                    case "<connect>":
                    {
                        c.Op = "connect";
                        c.From = NodeId(Next()); c.FromPort = PortText(Next());
                        c.To = NodeId(Next()); c.ToPort = PortText(Next());
                        if (c.From == null || c.To == null || c.FromPort == null || c.ToPort == null) problem = "a <connect> with an unknown node or port";
                        break;
                    }
                    case "<set>":
                    {
                        c.Op = "set_value"; c.Node = NodeId(Next()); c.Field = Text(Next()); c.Value = Text(Next());
                        if (c.Node == null || c.Field == null || c.Value == null) problem = "a <set> with an unknown node, field or value";
                        break;
                    }
                    case "<delete>":
                    case "<focus>":
                    {
                        c.Op = word == "<delete>" ? "delete_node" : "focus"; c.Node = NodeId(Next());
                        if (c.Node == null) problem = word + " with an unknown node";
                        break;
                    }
                    default:
                        problem = "an unexpected " + word;
                        break;
                }
                if (problem != null)
                {
                    problems.Add(problem);
                    // Skip to the next command word.
                    while (Peek() != null && !IsCommand(Peek())) i++;
                    continue;
                }
                commands.Add(c);
            }
            return commands;
        }

        private static bool IsCommand(string word) => word == "<end>" || word == "<add>" || word == "<add_signal>" || word == "<add_variable>"
            || word == "<add_behavior_ref>" || word == "<add_behavior>" || word == "<connect>" || word == "<set>" || word == "<delete>" || word == "<focus>";

        #endregion

        #region Saying what it did

        /// <summary>
        /// A short sentence about the commands, for Sprocket to say: the "talker", made from templates so it needs no
        /// model ("I'll add On Update and Jump, and connect On Update to Jump.").
        /// </summary>
        public string Describe(MacroState state, IList<MacroCommand> commands)
        {
            if (commands.Count == 0)
                return "Hmm, I'm not sure what to change for that.";
            var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var node in state.Nodes)
                names[node.Id] = string.IsNullOrEmpty(node.Name) ? Nice(node.Type) : node.Name;
            var adds = new List<string>();
            var parts = new List<string>();
            int connects = 0;
            foreach (var c in commands)
            {
                switch (c.Op)
                {
                    case "add_node": names[c.Id] = Nice(c.Type); adds.Add(Nice(c.Type)); break;
                    case "add_signal_node": names[c.Id] = (c.Mode == "send" ? "Send " : "On ") + Nice(c.Signal); adds.Add(names[c.Id]); break;
                    case "add_variable_node": names[c.Id] = (c.Mode == "set" ? "Set " : "Get ") + Nice(c.Variable); adds.Add(names[c.Id]); break;
                    case "add_behavior_node": names[c.Id] = Nice(c.Behavior); adds.Add(Nice(c.Behavior)); break;
                    case "add_behavior": parts.Add("add the " + Nice(c.Behavior) + " Behavior"); break;
                    case "connect": connects++; break;
                    case "set_value": parts.Add("set " + Nice(c.Field) + " to " + c.Value); break;
                    case "delete_node": parts.Add("remove " + Name(names, c.Node)); break;
                    case "focus": parts.Add("show you " + Name(names, c.Node)); break;
                }
            }
            if (adds.Count > 0) parts.Insert(0, "add " + Join(adds));
            if (connects == 1)
            {
                var link = commands.First(c => c.Op == "connect");
                parts.Add("connect " + Name(names, link.From) + " to " + Name(names, link.To));
            }
            else if (connects > 1)
                parts.Add("wire them together");
            string sentence = "I'll " + Join(parts) + ".";
            return sentence;
        }

        private static string Name(Dictionary<string, string> names, string id) => id != null && names.TryGetValue(id, out var name) ? name : "it";

        private static string Join(List<string> items)
            => items.Count <= 1 ? (items.Count == 1 ? items[0] : "") : string.Join(", ", items.Take(items.Count - 1)) + " and " + items[items.Count - 1];

        /// <summary>"OnUpdateNode" -> "On Update".</summary>
        public static string Nice(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            if (name.EndsWith("Node", StringComparison.Ordinal) && name.Length > 4) name = name.Substring(0, name.Length - 4);
            if (name.Contains(' ')) return name;
            return Regex.Replace(name, "(?<=[a-z0-9])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])", " ");
        }

        #endregion

        #region Grammar

        /// <summary>
        /// A llama.cpp grammar that only lets the model write well-formed answers: commands made of the vocabulary's
        /// words, references and quoted names, ending in &lt;end&gt;.
        /// </summary>
        public string Grammar()
        {
            string Alt(IEnumerable<string> words) => string.Join(" | ", words.Select(w => "\"" + w.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\""));
            var nodes = Vocabulary.Tokens.Where(t => t.Kind == "node").Select(t => t.Text).ToList();
            var behaviors = Vocabulary.Tokens.Where(t => t.Kind == "behavior").Select(t => t.Text).ToList();
            var text = new StringBuilder();
            text.Append("root ::= ( ws command )* ws \"<end>\"\n");
            text.Append("command ::= add | addsignal | addvariable | addref | addbehavior | connect | set | delete | focus\n");
            text.Append("add ::= \"<add>\" ws new ws nodetype\n");
            text.Append("addsignal ::= \"<add_signal>\" ws new ws ref ws string ws ( \"<mode:send>\" | \"<mode:receive>\" )\n");
            text.Append("addvariable ::= \"<add_variable>\" ws new ws ref ws string ws ( \"<mode:get>\" | \"<mode:set>\" )\n");
            text.Append("addref ::= \"<add_behavior_ref>\" ws new ws ref\n");
            text.Append("addbehavior ::= \"<add_behavior>\" ws behaviortype ws ref\n");
            text.Append("connect ::= \"<connect>\" ws handle ws port ws handle ws port\n");
            text.Append("set ::= \"<set>\" ws handle ws string ws string\n");
            text.Append("delete ::= \"<delete>\" ws handle\n");
            text.Append("focus ::= \"<focus>\" ws handle\n");
            text.Append("handle ::= ref | new\n");
            text.Append("port ::= \"<flow>\" | string\n");
            text.Append("ref ::= \"<ref:\" [1-9] [0-9]? \">\"\n");
            text.Append("new ::= \"<new:\" [1-9] [0-9]? \">\"\n");
            text.Append("nodetype ::= ").Append(nodes.Count > 0 ? Alt(nodes) : "\"<none>\"").Append('\n');
            text.Append("behaviortype ::= ").Append(behaviors.Count > 0 ? Alt(behaviors) : "\"<none>\"").Append('\n');
            text.Append("string ::= \"\\\"\" ( [^\"\\\\\\x00-\\x1F] | \"\\\\\" [\"\\\\nt] )* \"\\\"\"\n");
            text.Append("ws ::= [ \\t\\n]*\n");
            return text.ToString();
        }

        #endregion

        #region Text helpers

        public static string Quote(string text)
        {
            var result = new StringBuilder("\"");
            foreach (char ch in OneLine(text ?? ""))
            {
                if (ch == '"' || ch == '\\') result.Append('\\');
                result.Append(ch);
            }
            return result.Append('"').ToString();
        }

        public static string Unquote(string quoted)
        {
            var result = new StringBuilder();
            for (int i = 1; i < quoted.Length - 1; i++)
            {
                char ch = quoted[i];
                if (ch == '\\' && i + 1 < quoted.Length - 1) { char next = quoted[++i]; result.Append(next == 'n' ? ' ' : next == 't' ? ' ' : next); }
                else result.Append(ch);
            }
            return result.ToString();
        }

        private static string OneLine(string text) => Regex.Replace(text ?? "", "\\s+", " ").Trim();

        private static bool Same(string a, string b) => string.Equals((a ?? "").Replace(" ", ""), (b ?? "").Replace(" ", ""), StringComparison.OrdinalIgnoreCase);

        #endregion
    }
}
