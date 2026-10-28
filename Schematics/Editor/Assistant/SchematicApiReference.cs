using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Remedy.Schematics.Assistant
{
    /// <summary>
    /// Keeps the assistant's API reference in step with the code: after every recompile, every [SchematicDoc] (nodes,
    /// Behaviors, Variables, Signals), and every graph offered as a function (In Library: documented the same way from
    /// its description and Parameters), is gathered and handed to the assistant's server, which adds whatever it
    /// doesn't have yet (or that changed). Sync API Reference does it on demand, after editing a function. When the server isn't running, the list waits in Agent~/reference/incoming.json and
    /// is taken in when it starts.
    /// </summary>
    [InitializeOnLoad]
    internal static class SchematicApiReference
    {
        private const string HashKey = "Schematics.Assistant.ReferenceHash";

        static SchematicApiReference()
        {
            // After the editor settles from the reload.
            EditorApplication.delayCall += () => _ = SyncAsync(force: false);
        }

        [MenuItem("Tools/Remedy/Schematics/Assistant/Sync API Reference", priority = 31)]
        private static void SyncMenu() => _ = SyncAsync(force: true);

        /// <summary>One entry per documented piece: its key, kind, a hash of its JSON, and the JSON.</summary>
        public static List<(string key, string kind, string hash, string doc)> Collect()
        {
            var entries = new List<(string, string, string, string)>();
            foreach (var type in TypeCache.GetTypesWithAttribute<SchematicDocAttribute>())
            {
                var doc = type.GetCustomAttribute<SchematicDocAttribute>(false);
                if (doc == null) continue;
                string kind = typeof(SchematicBehavior).IsAssignableFrom(type) ? "behavior" : "node";
                entries.Add(($"{kind}:{type.FullName}", kind, Hash(doc.Json), doc.Json));
            }
            foreach (var field in TypeCache.GetFieldsWithAttribute<SchematicDocAttribute>())
            {
                var doc = field.GetCustomAttribute<SchematicDocAttribute>(false);
                if (doc == null) continue;
                string kind = field.FieldType.Name.StartsWith("SignalHandle") ? "signal" : "variable";
                entries.Add(($"{kind}:{field.DeclaringType?.FullName}.{field.Name}", kind, Hash(doc.Json), doc.Json));
            }
            foreach (var graph in LibraryFunctions())
            {
                string doc = GraphFunctions.Doc(graph);
                entries.Add(($"function:{graph.Uid}", "function", Hash(doc), doc));
            }
            entries.Sort((a, b) => string.CompareOrdinal(a.Item1, b.Item1));
            return entries;
        }

        // The graphs offered as functions. Their files are read as text first (a library graph's says InLibrary: 1),
        // so only those are loaded.
        private static IEnumerable<SchematicGraph> LibraryFunctions()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:" + nameof(SchematicGraph)))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string text;
                try { text = File.ReadAllText(path); }
                catch { continue; }
                if (!text.Contains("InLibrary: 1"))
                    continue;
                var graph = AssetDatabase.LoadAssetAtPath<SchematicGraph>(path);
                if (graph != null && graph.Function.InLibrary)
                    yield return graph;
            }
        }

        private static string Hash(string text)
        {
            using var md5 = MD5.Create();
            return BitConverter.ToString(md5.ComputeHash(Encoding.UTF8.GetBytes(text ?? ""))).Replace("-", "").ToLowerInvariant();
        }

        private static string Manifest(List<(string key, string kind, string hash, string doc)> entries)
        {
            var json = new AssistantJson();
            json.BeginArray();
            foreach (var (key, kind, hash, doc) in entries)
                json.BeginObject().String("key", key).String("kind", kind).String("hash", hash).String("doc", doc).EndObject();
            json.EndArray();
            return json.ToString();
        }

        private static async System.Threading.Tasks.Task SyncAsync(bool force)
        {
            string agent = AssistantServer.AgentFolder;
            if (agent == null)
                return;
            var entries = Collect();
            string manifest = Manifest(entries);
            string hash = Hash(manifest);
            bool changed = force || SessionState.GetString(HashKey, "") != hash;
            if (!changed)
                return;

            // Waiting for the server either way: it reads this when it starts.
            try
            {
                string folder = Path.Combine(agent, "reference");
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, "incoming.json"), manifest);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[Assistant] Couldn't write the API reference: " + exception.Message);
            }

            if (await AssistantServer.IsHealthyAsync())
            {
                string reply = await AssistantServer.SyncReferenceAsync(manifest);
                if (reply != null && (force || AssistantSettings.VerboseLogging))
                    Debug.Log($"[Assistant] API reference synced ({entries.Count} documented pieces): {reply}");
            }
            else if (force)
                Debug.Log($"[Assistant] {entries.Count} documented pieces saved for the assistant; it takes them in when it starts.");
            SessionState.SetString(HashKey, hash);
        }
    }

    /// <summary>The fields of a [SchematicDoc] that Sprocket shows.</summary>
    [Serializable]
    internal sealed class SchematicDocHeader
    {
        public string name, kind, description, behavior, direction, type, flow, notes, how;
        public string[] use_for;

        public static SchematicDocHeader Of(MemberInfo member)
        {
            var attribute = member?.GetCustomAttribute<SchematicDocAttribute>(false);
            if (attribute == null) return null;
            try { return JsonUtility.FromJson<SchematicDocHeader>(attribute.Json); }
            catch { return null; }
        }

        /// <summary>Description, plus what it's good for, for the speech bubble.</summary>
        public string Text
        {
            get
            {
                var text = new StringBuilder(description ?? "");
                if (use_for != null && use_for.Length > 0)
                    text.Append("\n<b>Good for:</b> ").Append(string.Join(", ", use_for)).Append('.');
                return text.ToString();
            }
        }
    }
}
