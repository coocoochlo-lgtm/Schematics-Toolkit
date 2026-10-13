using BlueGraph;
using BlueGraph.Editor;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Remedy.Schematics.Assistant.Macro
{
    /// <summary>
    /// Builds what training Sprocket's editor-token model needs, from the toolkit itself: the vocabulary (one token
    /// per node type and Behavior, namespaced by assembly) and thousands of example requests with the token answers
    /// they should get. Upload the two files to the training notebook (Agent~/Training). Every example is checked by
    /// decoding it back, so nothing broken goes into training.
    /// </summary>
    internal static class MacroTraining
    {
        private const string Menu = "Tools/Remedy/Schematics/Assistant/Training/";

        // Nodes made from IO Dock items have their own commands (add_signal, add_variable, add_behavior_ref).
        private static readonly HashSet<string> DockNodeTypes = new HashSet<string>
        {
            nameof(SendSignalNode), nameof(OnSignalReceivedNode), nameof(GetBehaviorVariableNode),
            nameof(SetBehaviorVariableNode), nameof(BehaviorReferenceNode), nameof(PrefabObjectNode),
        };

        public static string Folder
        {
            get
            {
                string agent = AssistantServer.AgentFolder;
                return Path.Combine(agent ?? Path.GetFullPath("Library/SprocketTraining"), "Training", "data");
            }
        }

        #region Vocabulary

        /// <summary>What a node type looks like to the generator (from BlueGraph's reflection).</summary>
        internal sealed class NodeInfo
        {
            public MacroToken Token;
            public bool IsEvent, FlowIn, FlowOut;
            public List<(string name, Type type)> Inputs = new List<(string, Type)>();
            public List<(string name, Type type)> Outputs = new List<(string, Type)>();
        }

        internal sealed class BehaviorInfo
        {
            public MacroToken Token;
            public List<(string name, string type)> Variables = new List<(string, string)>();
            public List<(string name, bool input)> Signals = new List<(string, bool)>();
        }

        /// <summary>The vocabulary of everything in the project, with what the generator needs to know about each.</summary>
        public static MacroVocabulary BuildVocabulary(out List<NodeInfo> nodes, out List<BehaviorInfo> behaviors)
        {
            var vocabulary = MacroVocabulary.Core();
            nodes = new List<NodeInfo>();
            behaviors = new List<BehaviorInfo>();

            foreach (var data in NodeReflection.GetNodeTypes().Values.OrderBy(d => d?.Type?.FullName, StringComparer.Ordinal))
            {
                if (data?.Type == null || data.Type.IsAbstract || DockNodeTypes.Contains(data.Type.Name))
                    continue;
                string assembly = data.Type.Assembly.GetName().Name;
                string name = string.IsNullOrEmpty(data.Name) ? data.Type.Name : data.Name;
                string category = data.Path != null ? string.Join(" ", data.Path) : "";
                var token = new MacroToken
                {
                    Text = MacroVocabulary.NodeToken(assembly, data.Type.Name), Kind = "node", Assembly = assembly,
                    TypeName = data.Type.Name, Name = name,
                    Description = $"{name} node {category} {data.Help}".Trim(),
                };
                vocabulary.Add(token);
                var info = new NodeInfo { Token = token, IsEvent = typeof(SchematicEventNode).IsAssignableFrom(data.Type) };
                foreach (var port in data.Ports)
                {
                    bool flow = port.Type == typeof(ActionPort);
                    if (port.Direction == PortDirection.Input)
                    {
                        if (flow) info.FlowIn = true; else info.Inputs.Add((port.Name, port.Type));
                    }
                    else
                    {
                        if (flow) info.FlowOut = true; else info.Outputs.Add((port.Name, port.Type));
                    }
                }
                nodes.Add(info);
            }

            foreach (var type in TypeCache.GetTypesDerivedFrom<SchematicBehavior>().OrderBy(t => t.FullName, StringComparer.Ordinal))
            {
                if (type.IsAbstract || type.IsGenericTypeDefinition)
                    continue;
                string assembly = type.Assembly.GetName().Name;
                var token = new MacroToken
                {
                    Text = MacroVocabulary.BehaviorToken(assembly, type.Name), Kind = "behavior", Assembly = assembly,
                    TypeName = type.Name, Name = MacroLanguage.Nice(type.Name),
                    Description = $"{MacroLanguage.Nice(type.Name)} behavior",
                };
                vocabulary.Add(token);
                var info = new BehaviorInfo { Token = token };
                var metadata = BehaviorMetadata.Get(type);
                if (metadata != null)
                {
                    foreach (var variable in metadata.Variables)
                        if (variable != null && !string.IsNullOrEmpty(variable.Name))
                            info.Variables.Add((variable.Name, variable.Type.Type != null ? variable.Type.Type.Name : "object"));
                    foreach (var signal in metadata.Signals)
                        if (signal != null && !string.IsNullOrEmpty(signal.Name))
                            info.Signals.Add((signal.Name, signal.Direction == SignalDirection.Input));
                }
                behaviors.Add(info);
            }
            return vocabulary;
        }

        [MenuItem(Menu + "Export Vocabulary", priority = 1)]
        private static void ExportVocabulary()
        {
            var vocabulary = BuildVocabulary(out var nodes, out var behaviors);
            Directory.CreateDirectory(Folder);
            string path = Path.Combine(Folder, "vocab.tsv");
            vocabulary.Save(path);
            Debug.Log($"[Sprocket training] Vocabulary: {vocabulary.Tokens.Count} tokens ({nodes.Count} node types, {behaviors.Count} Behaviors), fingerprint {vocabulary.Fingerprint()}. Saved to {path}");
            EditorUtility.RevealInFinder(path);
        }

        #endregion

        #region Dataset

        [MenuItem(Menu + "Generate Training Data…", priority = 0)]
        private static void GenerateMenu()
        {
            if (!EditorUtility.DisplayDialog("Generate Training Data",
                    "Makes the vocabulary and about 20,000 example requests with their token answers, from the node types and Behaviors in this project.\n\n"
                    + $"They go to {Folder}. Then open Agent~/Training/train_macro_model.ipynb in Google Colab (or Kaggle) and upload them.",
                    "Generate", "Cancel"))
                return;
            Generate(20000, seed: 1234);
        }

        public static void Generate(int count, int seed)
        {
            var vocabulary = BuildVocabulary(out var nodes, out var behaviors);
            var language = new MacroLanguage(vocabulary);
            var generator = new ExampleGenerator(language, nodes, behaviors, new System.Random(seed));
            Directory.CreateDirectory(Folder);
            vocabulary.Save(Path.Combine(Folder, "vocab.tsv"));

            var counts = new Dictionary<string, int>();
            int written = 0, rejected = 0;
            var seen = new HashSet<string>();
            string path = Path.Combine(Folder, "dataset.jsonl");
            try
            {
                using var file = new StreamWriter(path, false, new UTF8Encoding(false));
                for (int attempt = 0; written < count && attempt < count * 4; attempt++)
                {
                    if (attempt % 500 == 0 && EditorUtility.DisplayCancelableProgressBar("Generating training data", $"{written} of {count} examples", written / (float)count))
                        break;
                    var example = generator.Next();
                    if (example == null) { rejected++; continue; }
                    string prompt = language.Prompt(example.State, example.Request);
                    string answer = language.Encode(example.State, example.Commands);
                    // Everything must survive the round trip, or it's left out.
                    var problems = new List<string>();
                    var decoded = answer != null ? language.Decode(example.State, answer, out problems) : null;
                    if (answer == null || decoded == null || (problems != null && problems.Count > 0) || decoded.Count != example.Commands.Count || !seen.Add(prompt))
                    {
                        rejected++;
                        continue;
                    }
                    file.Write("{\"prompt\":\""); file.Write(AssistantJson.Escape(prompt));
                    file.Write("\",\"completion\":\""); file.Write(AssistantJson.Escape(answer));
                    file.Write("\",\"say\":\""); file.Write(AssistantJson.Escape(language.Describe(example.State, decoded)));
                    file.Write("\",\"kind\":\""); file.Write(example.Kind);
                    file.Write("\"}\n");
                    counts.TryGetValue(example.Kind, out int n);
                    counts[example.Kind] = n + 1;
                    written++;
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
            File.WriteAllText(Path.Combine(Folder, "README.txt"),
                $"vocab.tsv: {vocabulary.Tokens.Count} tokens, fingerprint {vocabulary.Fingerprint()}\n"
                + $"dataset.jsonl: {written} examples ({rejected} rejected while generating)\n"
                + string.Join("\n", counts.OrderBy(p => p.Key).Select(p => $"  {p.Key}: {p.Value}")) + "\n");
            Debug.Log($"[Sprocket training] {written} examples ({string.Join(", ", counts.OrderBy(p => p.Key).Select(p => $"{p.Key} {p.Value}"))}), "
                + $"{vocabulary.Tokens.Count} tokens. Saved to {Folder}. Next: Agent~/Training/train_macro_model.ipynb");
            EditorUtility.RevealInFinder(path);
        }

        internal sealed class Example
        {
            public string Kind;
            public MacroState State;
            public string Request;
            public List<MacroCommand> Commands;
        }

        /// <summary>
        /// Makes random but sensible editor states (a few nodes, objects with Behaviors) and requests about them,
        /// worded many ways, with the commands that carry them out. The notebook can reword them further.
        /// </summary>
        internal sealed class ExampleGenerator
        {
            private readonly MacroLanguage _language;
            private readonly List<NodeInfo> _nodes, _events, _actions;
            private readonly List<BehaviorInfo> _behaviors;
            private readonly System.Random _random;
            private readonly Dictionary<string, NodeInfo> _byName;

            private static readonly string[] ObjectNames = { "Player", "Enemy", "Door", "Chest", "Turret", "Camera", "Body", "Head", "Gun", "Light", "Platform", "Coin", "Boss", "NPC", "Car" };
            private static readonly string[] ChildNames = { "Body", "Arm", "Muzzle", "Visual", "Wheel", "Hand", "Eyes", "Sensor" };

            public ExampleGenerator(MacroLanguage language, List<NodeInfo> nodes, List<BehaviorInfo> behaviors, System.Random random)
            {
                _language = language;
                _nodes = nodes;
                _events = nodes.Where(n => n.IsEvent && n.FlowOut).ToList();
                _actions = nodes.Where(n => !n.IsEvent && n.FlowIn).ToList();
                _behaviors = behaviors;
                _random = random;
                _byName = new Dictionary<string, NodeInfo>(StringComparer.OrdinalIgnoreCase);
                foreach (var node in nodes)
                    _byName[node.Token.Name] = node;
            }

            private T Pick<T>(IList<T> items) => items[_random.Next(items.Count)];
            private bool Chance(double p) => _random.NextDouble() < p;

            // How people say a node's name: "On Update", "on update", "OnUpdate", "the On Update node".
            private string Say(string name)
            {
                switch (_random.Next(5))
                {
                    case 0: return name.ToLowerInvariant();
                    case 1: return name.Replace(" ", "");
                    case 2: return "the " + name + " node";
                    case 3: return "the " + name.ToLowerInvariant();
                    default: return name;
                }
            }

            private string A(string word) => "aeiouAEIOU".IndexOf(word.Length > 0 ? word[0] : 'x') >= 0 ? "an " + word : "a " + word;

            public Example Next()
            {
                if (_nodes.Count == 0) return null;
                var state = RandomState();
                int kind = _random.Next(12);
                switch (kind)
                {
                    case 0: return AddNode(state);
                    case 1: return EventThenAction(state);
                    case 2: return ConnectExisting(state);
                    case 3: return ActionAfterExisting(state);
                    case 4: return Delete(state);
                    case 5: return SetValue(state);
                    case 6: return Focus(state);
                    case 7: return SendSignal(state);
                    case 8: return ReceiveSignal(state);
                    case 9: return Variable(state);
                    case 10: return AddBehavior(state);
                    default: return Chain(state);
                }
            }

            #region States

            private MacroState RandomState()
            {
                var state = new MacroState();
                int nodeCount = _random.Next(0, 7);
                for (int i = 0; i < nodeCount; i++)
                    state.Nodes.Add(StateNodeFor(Pick(_nodes), "n" + (i + 1)));
                // Some existing flow links.
                foreach (var from in state.Nodes)
                    if (from.HasFlowOut && Chance(0.4))
                    {
                        var to = state.Nodes.Where(n => n != from && n.HasFlowIn).ToList();
                        if (to.Count > 0) from.Links.Add(("flow", Pick(to).Id, "flow"));
                    }
                int objectCount = _random.Next(0, 4);
                var usable = _behaviors.Where(b => b.Variables.Count > 0 || b.Signals.Count > 0).ToList();
                for (int i = 0; i < objectCount; i++)
                {
                    var obj = new StateObject { Name = i == 0 ? Pick(ObjectNames) : Pick(ChildNames) };
                    obj.Path = i == 0 ? "" : obj.Name;
                    if (state.Objects.Any(o => o.Path == obj.Path)) continue;
                    int behaviorCount = usable.Count == 0 ? 0 : _random.Next(0, 3);
                    for (int b = 0; b < behaviorCount; b++)
                    {
                        var info = Pick(usable);
                        if (obj.Behaviors.Any(x => x.Type == info.Token.TypeName)) continue;
                        var behavior = new StateBehavior { Type = info.Token.TypeName };
                        behavior.Variables.AddRange(info.Variables);
                        behavior.Signals.AddRange(info.Signals);
                        obj.Behaviors.Add(behavior);
                    }
                    state.Objects.Add(obj);
                }
                return state;
            }

            private static StateNode StateNodeFor(NodeInfo info, string id) => new StateNode
            {
                Id = id, Type = info.Token.Name, HasFlowIn = info.FlowIn, HasFlowOut = info.FlowOut,
                Inputs = info.Inputs.Select(p => p.name).ToList(), Outputs = info.Outputs.Select(p => p.name).ToList(),
            };

            // A node in the state the request can name without ambiguity (its type appears once).
            private StateNode Unique(MacroState state, Func<StateNode, bool> where)
            {
                var candidates = state.Nodes.Where(n => where(n) && state.Nodes.Count(o => o.Type == n.Type) == 1).ToList();
                return candidates.Count == 0 ? null : Pick(candidates);
            }

            private Example Make(string kind, MacroState state, string request, params MacroCommand[] commands)
                => new Example { Kind = kind, State = state, Request = request, Commands = commands.ToList() };

            private static MacroCommand Add(string id, NodeInfo node) => new MacroCommand { Op = "add_node", Id = id, Type = node.Token.Name };
            private static MacroCommand Flow(string from, string to) => new MacroCommand { Op = "connect", From = from, FromPort = "flow", To = to, ToPort = "flow" };

            #endregion

            #region Requests

            private Example AddNode(MacroState state)
            {
                var node = Pick(_nodes);
                string name = Say(node.Token.Name).Replace("the ", "").Replace(" node", "");
                string request = string.Format(Pick(new[]
                {
                    "add {0}", "add {1} node", "put down {1} node", "I need {1}", "create {1} node", "drop in {0}",
                    "can you add {1}?", "give me {1} node", "place {1} node", "new {0} node please", "add {0} to the graph",
                }), name, A(name));
                return Make("add_node", state, request, Add("a", node));
            }

            private string ActionPhrase(NodeInfo action)
            {
                string name = Say(action.Token.Name).Replace("the ", "");
                return string.Format(Pick(new[] { "{0}", "do {0}", "run {0}", "use {0}", "add {0}", "call {0}" }), name);
            }

            private Example EventThenAction(MacroState state)
            {
                if (_events.Count == 0 || _actions.Count == 0) return null;
                var evt = Pick(_events);
                var action = Pick(_actions);
                string e = Say(evt.Token.Name).Replace("the ", "");
                string request = string.Format(Pick(new[]
                {
                    "when {0}, {1}", "on {0} {1}", "every time {0} happens, {1}", "{1} when {0}", "hook up {0} to {1}",
                    "make it {1} on {0}", "add {0} and connect it to {1}", "I want {1} to happen on {0}",
                }), e, ActionPhrase(action));
                return Make("event_action", state, request, Add("a", evt), Add("b", action), Flow("a", "b"));
            }

            private Example ConnectExisting(MacroState state)
            {
                var from = Unique(state, n => n.HasFlowOut);
                if (from == null) return null;
                var to = Unique(state, n => n != from && n.HasFlowIn && !from.Links.Any(l => l.to == n.Id));
                if (to == null) return null;
                string request = string.Format(Pick(new[]
                {
                    "connect {0} to {1}", "wire {0} into {1}", "link {0} and {1}", "hook {0} up to {1}", "run {1} after {0}",
                    "make {0} trigger {1}", "connect the flow from {0} to {1}",
                }), Say(from.Type), Say(to.Type));
                return Make("connect", state, request, Flow(from.Id, to.Id));
            }

            private Example ActionAfterExisting(MacroState state)
            {
                var from = Unique(state, n => n.HasFlowOut);
                if (from == null || _actions.Count == 0) return null;
                var action = Pick(_actions);
                string request = string.Format(Pick(new[]
                {
                    "after {0}, {1}", "then {1} after {0}", "make {0} {1}", "add {2} after {0}", "when {0} runs, {1}",
                }), Say(from.Type), ActionPhrase(action), Say(action.Token.Name).Replace("the ", ""));
                return Make("after", state, request, Add("a", action), Flow(from.Id, "a"));
            }

            private Example Delete(MacroState state)
            {
                var node = Unique(state, n => true);
                if (node == null) return null;
                string request = string.Format(Pick(new[] { "delete {0}", "remove {0}", "get rid of {0}", "delete the {0} node", "I don't need {0} anymore" }), Say(node.Type));
                return Make("delete", state, request, new MacroCommand { Op = "delete_node", Node = node.Id });
            }

            private Example SetValue(MacroState state)
            {
                var candidates = new List<(StateNode node, string port, Type type)>();
                foreach (var node in state.Nodes)
                {
                    if (state.Nodes.Count(o => o.Type == node.Type) != 1 || !_byName.TryGetValue(node.Type, out var info)) continue;
                    foreach (var (name, type) in info.Inputs)
                        if (type == typeof(float) || type == typeof(int) || type == typeof(bool) || type == typeof(string))
                            candidates.Add((node, name, type));
                }
                if (candidates.Count == 0) return null;
                var (target, port, portType) = Pick(candidates);
                string value = portType == typeof(float) ? (_random.Next(1, 200) / 4f).ToString(System.Globalization.CultureInfo.InvariantCulture)
                    : portType == typeof(int) ? _random.Next(0, 100).ToString(System.Globalization.CultureInfo.InvariantCulture)
                    : portType == typeof(bool) ? (Chance(0.5) ? "true" : "false")
                    : Pick(new[] { "Hello", "Game Over", "Player", "Level 2", "Door opened", "ready" });
                string request = string.Format(Pick(new[]
                {
                    "set {1} on {0} to {2}", "make {0}'s {1} {2}", "change {1} to {2} on {0}", "{0} {1} should be {2}", "set the {1} of {0} to {2}",
                }), Say(target.Type), port.ToLowerInvariant(), value);
                return Make("set_value", state, request, new MacroCommand { Op = "set_value", Node = target.Id, Field = port, Value = value });
            }

            private Example Focus(MacroState state)
            {
                var node = Unique(state, n => true);
                if (node == null) return null;
                string request = string.Format(Pick(new[] { "show me {0}", "where is {0}?", "find {0}", "take me to {0}", "focus on {0}" }), Say(node.Type));
                return Make("focus", state, request, new MacroCommand { Op = "focus", Node = node.Id });
            }

            private (StateObject obj, StateBehavior behavior, string signal)? PickSignal(MacroState state, bool input)
            {
                var all = state.Objects.SelectMany(o => o.Behaviors.SelectMany(b => b.Signals.Where(s => s.input == input).Select(s => (o, b, s.name)))).ToList();
                if (all.Count == 0) return null;
                var picked = Pick(all);
                // Ambiguous when another Behavior has a Signal of the same name.
                if (all.Count(x => string.Equals(x.name, picked.name, StringComparison.OrdinalIgnoreCase)) > 1) return null;
                return picked;
            }

            private static string Words(string name) => MacroLanguage.Nice(name).ToLowerInvariant();

            private Example SendSignal(MacroState state)
            {
                var picked = PickSignal(state, input: true);
                if (picked == null) return null;
                var (obj, behavior, signal) = picked.Value;
                string request = string.Format(Pick(new[] { "send {0}", "trigger {0} on {1}", "fire the {0} signal", "make {1} {0}", "send the {0} signal to {1}" }), Words(signal), obj.Name);
                return Make("send_signal", state, request,
                    new MacroCommand { Op = "add_signal_node", Id = "a", Behavior = behavior.Type, Path = obj.Path, Signal = signal, Mode = "send" });
            }

            private Example ReceiveSignal(MacroState state)
            {
                var picked = PickSignal(state, input: false);
                if (picked == null || _actions.Count == 0) return null;
                var (obj, behavior, signal) = picked.Value;
                var action = Pick(_actions);
                string request = string.Format(Pick(new[] { "when {0} happens, {1}", "on {0}, {1}", "when {2} sends {0}, {1}", "{1} when {0} fires", "react to {0} by {1}" }),
                    Words(signal), ActionPhrase(action), obj.Name);
                return Make("receive_signal", state, request,
                    new MacroCommand { Op = "add_signal_node", Id = "a", Behavior = behavior.Type, Path = obj.Path, Signal = signal, Mode = "receive" },
                    Add("b", action), Flow("a", "b"));
            }

            private Example Variable(MacroState state)
            {
                var all = state.Objects.SelectMany(o => o.Behaviors.SelectMany(b => b.Variables.Select(v => (o, b, v.name)))).ToList();
                if (all.Count == 0) return null;
                var (obj, behavior, variable) = Pick(all);
                if (all.Count(x => string.Equals(x.name, variable, StringComparison.OrdinalIgnoreCase)) > 1) return null;
                bool set = Chance(0.5);
                string request = set
                    ? string.Format(Pick(new[] { "set {1}'s {0}", "change the {0} variable", "I want to set {0}", "write to {0} on {1}" }), Words(variable), obj.Name)
                    : string.Format(Pick(new[] { "get {0} from {1}", "read {1}'s {0}", "I need the {0} value", "get the {0} variable" }), Words(variable), obj.Name);
                return Make(set ? "set_variable" : "get_variable", state, request,
                    new MacroCommand { Op = "add_variable_node", Id = "a", Behavior = behavior.Type, Path = obj.Path, Variable = variable, Mode = set ? "set" : "get" });
            }

            private Example AddBehavior(MacroState state)
            {
                if (state.Objects.Count == 0 || _behaviors.Count == 0) return null;
                var obj = Pick(state.Objects);
                var behavior = Pick(_behaviors);
                if (obj.Behaviors.Any(b => b.Type == behavior.Token.TypeName)) return null;
                string name = behavior.Token.Name;
                string request = string.Format(Pick(new[] { "add {0} to {1}", "give {1} {0}", "put {0} on {1}", "add the {2} behavior to {1}" }),
                    A(name), obj.Name, name);
                return Make("add_behavior", state, request, new MacroCommand { Op = "add_behavior", Behavior = behavior.Token.TypeName, Path = obj.Path });
            }

            private Example Chain(MacroState state)
            {
                if (_events.Count == 0 || _actions.Count < 2) return null;
                var evt = Pick(_events);
                var first = Pick(_actions);
                var second = Pick(_actions);
                if (!first.FlowOut || first == second) return null;
                string request = string.Format(Pick(new[] { "when {0}, {1} and then {2}", "on {0} {1}, then {2}", "{1} then {2} every time {0}" }),
                    Say(evt.Token.Name).Replace("the ", ""), ActionPhrase(first), ActionPhrase(second));
                return Make("chain", state, request, Add("a", evt), Add("b", first), Add("c", second), Flow("a", "b"), Flow("b", "c"));
            }

            #endregion
        }

        #endregion
    }
}
