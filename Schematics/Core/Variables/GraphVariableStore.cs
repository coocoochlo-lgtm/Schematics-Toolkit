using System;
using System.Collections.Generic;
using UnityEngine;

namespace Remedy.Schematics
{
    /// <summary>
    /// One Schematic Object's values for one graph's Variables. Made the first time something reads or writes them
    /// while the object is spawned, from the graph's defaults and the object's overrides; thrown away when it despawns,
    /// so every spawn starts fresh (like Behavior Variables).
    /// </summary>
    public sealed class GraphVariableStore
    {
        public readonly SchematicObject Owner;
        public readonly SchematicGraph Graph;
        private object[] _values;
        private Type[] _types;

        /// <summary>Raised after a Variable changes (index, new value). Networking and the editor listen.</summary>
        public event Action<int, object> Changed;

        internal GraphVariableStore(SchematicObject owner, SchematicGraph graph)
        {
            Owner = owner;
            Graph = graph;
            Reset();
        }

        public int Count => _values.Length;

        /// <summary>
        /// A Function Graph call's own Variables (they're its working values, not lasting state): made fresh the first
        /// time the call uses them and kept on the call, so calls running at once (one waiting, or a function calling
        /// itself) don't share them. Null outside a call of that graph.
        /// </summary>
        internal static GraphVariableStore ForCall(SchematicGraph graph, SchematicObject owner)
        {
            if (graph is not FunctionGraph)
                return null;
            var call = Flow.Current?.ExecutingScope?.FindCall(graph);
            if (call == null)
                return null;
            if (call.Variables is GraphVariableStore store && store.Owner == owner)
            {
                store.Refresh();
                return store;
            }
            store = new GraphVariableStore(owner, graph);
            call.Variables = store;
            return store;
        }

        /// <summary>Back to the starting values: the object's overrides, else the graph's defaults.</summary>
        public void Reset()
        {
            var variables = Graph.Variables;
            _values = new object[variables.Count];
            _types = new Type[variables.Count];
            for (int i = 0; i < variables.Count; i++)
            {
                var variable = variables[i];
                var type = variable?.ValueType;
                _types[i] = type;
                if (type == null)
                    continue;
                var start = Owner != null ? Owner.FindGraphOverride(Graph, variable.Id) : null;
                _values[i] = (start ?? variable.Default)?.Get(type) ?? (type.IsValueType ? Activator.CreateInstance(type) : null);
            }
        }

        public int IndexOf(string id) => Graph.IndexOfVariable(id);

        public Type TypeOf(int index) => index >= 0 && index < _types.Length ? _types[index] : null;

        public object Get(int index) => index >= 0 && index < _values.Length ? _values[index] : null;

        public object Get(string id) => Get(IndexOf(id));

        public void Set(int index, object value)
        {
            if (index < 0 || index >= _values.Length)
                return;
            var type = _types[index];
            if (type != null && value != null && !type.IsInstanceOfType(value))
            {
                try { value = Convert.ChangeType(value, type); }
                catch { value = type.IsValueType ? Activator.CreateInstance(type) : null; }
            }
            if (Equals(_values[index], value))
                return;
            _values[index] = value;
            Changed?.Invoke(index, value);
        }

        public void Set(string id, object value) => Set(IndexOf(id), value);

        public T Get<T>(string id) => Get(id) is T typed ? typed : default;

        // Graphs can gain Variables while the game runs (in the editor): grow to fit, keeping current values.
        internal void Refresh()
        {
            if (_values.Length == Graph.Variables.Count)
                return;
            var old = _values;
            Reset();
            Array.Copy(old, _values, Math.Min(old.Length, _values.Length));
        }
    }
}
