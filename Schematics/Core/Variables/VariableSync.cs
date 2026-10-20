using System;
using System.Collections.Generic;
using UnityEngine;

namespace Remedy.Schematics
{
    /// <summary>
    /// The Variables marked Network Sync (Behavior Variables in their Behavior Metadata, graph Variables in their graph),
    /// for the networking layer: it asks the authority's copy what changed since the last send, and applies what arrives
    /// on everyone else's. Changes are found by comparing with what was last sent, so Variables written any way (code,
    /// graphs, the Inspector) are all caught. Nothing here knows about Netcode.
    /// </summary>
    /// <remarks>
    /// Keys: "b|path|Behavior.FullName|Variable" for Behavior Variables, "g|variableId" for graph Variables (ids are
    /// unique across graphs).
    /// </remarks>
    public static class VariableSync
    {
        private static readonly Dictionary<SchematicObject, Dictionary<string, object>> _sent = new();

        /// <summary>Whether an object has any Variable to sync (so the networking layer can skip the rest cheaply).</summary>
        public static bool HasAny(SchematicObject owner)
        {
            foreach (var _ in Enumerate(owner))
                return true;
            return false;
        }

        /// <summary>
        /// The networked Variables whose value changed since the last call for this object (all of them with
        /// <paramref name="all"/>, e.g. for a peer that just joined).
        /// </summary>
        public static void CollectChanges(SchematicObject owner, List<(string key, object value)> changes, bool all = false)
        {
            if (owner == null)
                return;
            if (!_sent.TryGetValue(owner, out var sent))
                _sent[owner] = sent = new Dictionary<string, object>();

            foreach (var (key, value, _) in Enumerate(owner))
            {
                bool known = sent.TryGetValue(key, out var previous);
                if (all || !known || !Equals(previous, value))
                {
                    changes.Add((key, value));
                    sent[key] = value;
                }
            }
        }

        /// <summary>The type of a networked Variable, to read what arrives (null if this object doesn't have it).</summary>
        public static Type TypeOf(SchematicObject owner, string key)
        {
            foreach (var (k, _, type) in Enumerate(owner))
                if (k == key)
                    return type;
            return null;
        }

        /// <summary>Sets a networked Variable from what the authority sent.</summary>
        public static void Apply(SchematicObject owner, string key, object value)
        {
            if (owner == null || string.IsNullOrEmpty(key))
                return;

            if (key.StartsWith("g|"))
            {
                string id = key.Substring(2);
                foreach (var store in GraphStoresWith(owner, id))
                    store.Set(store.IndexOf(id), value);
            }
            else if (key.StartsWith("b|"))
            {
                var parts = key.Split('|');
                if (parts.Length < 4)
                    return;
                foreach (var instance in owner.BehaviorInstances)
                {
                    if (!instance.IsSpawned || instance.Behavior.GetType().FullName != parts[2] || PathOf(owner, instance.GameObject) != parts[1])
                        continue;
                    int index = instance.Runtime.IndexOfVariable(parts[3]);
                    if (index >= 0)
                        instance.Runtime.SetVariableBoxed(instance, index, value);
                }
            }

            // What arrived counts as sent, should this peer become the authority later.
            if (!_sent.TryGetValue(owner, out var sent))
                _sent[owner] = sent = new Dictionary<string, object>();
            sent[key] = value;
        }

        /// <summary>Forgets an object (when it despawns), so its next spawn sends everything again.</summary>
        public static void Forget(SchematicObject owner) => _sent.Remove(owner);

        // Every networked Variable of a spawned object: (key, current value, type).
        private static IEnumerable<(string key, object value, Type type)> Enumerate(SchematicObject owner)
        {
            if (owner == null || !owner.IsSpawned)
                yield break;

            foreach (var instance in owner.BehaviorInstances)
            {
                var metadata = instance.Runtime.Metadata;
                if (!instance.IsSpawned || metadata == null)
                    continue;
                foreach (var definition in metadata.Variables)
                {
                    if (definition == null || !definition.Networked)
                        continue;
                    int index = instance.Runtime.IndexOfVariable(definition.Name);
                    if (index < 0)
                        continue;
                    yield return ($"b|{PathOf(owner, instance.GameObject)}|{instance.Behavior.GetType().FullName}|{definition.Name}",
                        instance.Runtime.GetVariableBoxed(instance, index), definition.Type.Type);
                }
            }

            // Graph Variables: the object's own graph (made now if nothing has touched it yet), and any others it runs.
            if (owner.SchematicGraph != null && HasNetworked(owner.SchematicGraph))
                owner.GraphVariables(owner.SchematicGraph);
            foreach (var store in owner.GraphStores)
            {
                var variables = store.Graph.Variables;
                for (int i = 0; i < variables.Count && i < store.Count; i++)
                    if (variables[i] != null && variables[i].Networked)
                        yield return ("g|" + variables[i].Id, store.Get(i), variables[i].ValueType);
            }
        }

        private static bool HasNetworked(SchematicGraph graph)
        {
            foreach (var variable in graph.Variables)
                if (variable != null && variable.Networked)
                    return true;
            return false;
        }

        private static IEnumerable<GraphVariableStore> GraphStoresWith(SchematicObject owner, string id)
        {
            if (owner.SchematicGraph != null && owner.SchematicGraph.IndexOfVariable(id) >= 0)
                owner.GraphVariables(owner.SchematicGraph);
            foreach (var store in owner.GraphStores)
                if (store.IndexOf(id) >= 0)
                    yield return store;
        }

        private static string PathOf(SchematicObject owner, GameObject target)
        {
            if (target == null || target == owner.gameObject)
                return "";
            var parts = new List<string>();
            for (var t = target.transform; t != null && t != owner.transform; t = t.parent)
                parts.Insert(0, t.name);
            return string.Join("/", parts);
        }
    }
}
