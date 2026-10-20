using Remedy.Framework;
using System;
using System.IO;
using UnityEngine;

public partial class SignalData
{
    /// <summary>
    /// Makes the Signal carry exactly one value of the given type (keeping its parameter's name, or "Value"), for
    /// Signals whose value is decided elsewhere, like a Remedy Input's (by its Input Action's control type). True if
    /// anything changed. Types a Signal can't carry are left alone.
    /// </summary>
    public bool SetSingleParameter(Type type, string defaultName = "Value")
    {
        if (type == null || !Union.ReverseTypeLookup.TryGetValue(type, out var kind))
            return false;

        var contents = Parameters.Contents;
        if (contents.Count == 1 && contents[0].Value.Type == kind)
            return false;

        string name = contents.Count > 0 && !string.IsNullOrWhiteSpace(contents[0].Name) ? contents[0].Name : defaultName;
        contents.Clear();
        contents.Add(new Parameter(name, new Union(type)));
        RuntimeArgs = new Union[1];
        return true;
    }

#if UNITY_EDITOR
    /// <summary>
    /// Editor only: renames the Signal everywhere it shows (its Name, its asset, and the asset's file, which must match
    /// for Unity), for Signals named after something else, like a Remedy Input's (after its input). True if renamed.
    /// </summary>
    public bool EditorRename(string newName)
    {
        newName = newName?.Trim();
        if (string.IsNullOrEmpty(newName) || (Name == newName && name == FileSafe(newName)))
            return false;

        string oldName = name;
        UnityEditor.Undo.RecordObject(this, "Rename Signal");
        string path = UnityEditor.AssetDatabase.GetAssetPath(this);
        if (!string.IsNullOrEmpty(path) && UnityEditor.AssetDatabase.IsMainAsset(this))
        {
            string error = UnityEditor.AssetDatabase.RenameAsset(path, FileSafe(newName));
            if (!string.IsNullOrEmpty(error))
                Debug.LogWarning($"Couldn't rename the Signal's file to \"{newName}\": {error}");
        }
        else
            name = newName;

        Name = newName;
        UnityEditor.EditorUtility.SetDirty(this);
        RetitleNodes(oldName, name);
        return true;
    }

    // Send / On Received nodes for this Signal still titled after its old name ("On Output") take the new one. Titles
    // changed by hand are left alone.
    private void RetitleNodes(string oldName, string newName)
    {
        if (oldName == newName)
            return;
        foreach (var guid in UnityEditor.AssetDatabase.FindAssets("t:" + nameof(Remedy.Schematics.SchematicGraph)))
        {
            var graph = UnityEditor.AssetDatabase.LoadAssetAtPath<Remedy.Schematics.SchematicGraph>(UnityEditor.AssetDatabase.GUIDToAssetPath(guid));
            if (graph == null)
                continue;
            bool changed = false;
            foreach (var node in graph.Nodes)
            {
                string title = node switch
                {
                    global::OnSignalReceivedNode receive when receive.Signal == this && receive.Name == "On " + oldName => "On " + newName,
                    global::SendSignalNode send when send.Signal == this && send.Name == "Send " + oldName => "Send " + newName,
                    _ => null,
                };
                if (title == null)
                    continue;
                if (!changed)
                    UnityEditor.Undo.RecordObject(graph, "Rename Signal");
                node.Name = title;
                changed = true;
            }
            if (changed)
                UnityEditor.EditorUtility.SetDirty(graph);
        }
    }

    // A file name can't hold every character a name can ("Jump/Dash"): those become underscores.
    private static string FileSafe(string text)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
            text = text.Replace(c, '_');
        return text;
    }
#endif
}
