using System.Collections.Generic;
using Remedy.Schematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// In Play Mode, a Signal's rows in the IO Dock flash when it's sent (or received), fading out over a moment: for the
/// object the graph is on (or every object, when it's on a prefab or the Signal goes to everyone). Rows sign up with
/// <see cref="Track"/>; the Signals sent in a frame are flashed together on the next editor update.
/// </summary>
[InitializeOnLoad]
internal static class SignalFlash
{
    private const float Seconds = 0.6f;
    private static readonly Color Lit = new(1f, 0.72f, 0.25f, 0.85f);

    private static readonly Dictionary<SignalData, List<VisualElement>> _rows = new();
    private static readonly Dictionary<SignalData, bool> _sent = new();
    private static readonly Dictionary<VisualElement, IVisualElementScheduledItem> _fading = new();

    static SignalFlash()
    {
        SignalData.EditorSent += OnSent;
        EditorApplication.update += Flush;
    }

    /// <summary>A row of a Signal's, to flash while it's on screen.</summary>
    public static void Track(VisualElement row, SignalData signal)
    {
        if (row == null || signal == null)
            return;
        row.RegisterCallback<AttachToPanelEvent>(_ =>
        {
            if (!_rows.TryGetValue(signal, out var rows))
                _rows[signal] = rows = new List<VisualElement>();
            if (!rows.Contains(row))
                rows.Add(row);
        });
        row.RegisterCallback<DetachFromPanelEvent>(_ =>
        {
            if (_rows.TryGetValue(signal, out var rows))
            {
                rows.Remove(row);
                if (rows.Count == 0)
                    _rows.Remove(signal);
            }
        });
    }

    private static void OnSent(SignalData signal, int uid)
    {
        if (signal == null || !_rows.ContainsKey(signal))
            return;
        // (Kept until the next update: the dock isn't touched from inside the game's frame.)
        _sent[signal] = (_sent.TryGetValue(signal, out var any) && any) || ForShownObject(uid);
    }

    // Sent to everyone, or to the object the graph is on (any object, when it isn't on one).
    private static bool ForShownObject(int uid)
    {
        if (uid == 0)
            return true;
        var shown = SchematicGraphEditorWindow.ActiveDock;
        var instance = shown != null ? shown.FollowedInstance : null;
        if (instance == null)
            return true;
        return SignalData.UIDResolver != null && SignalData.UIDResolver(instance) == uid;
    }

    private static void Flush()
    {
        if (_sent.Count == 0)
            return;
        foreach (var pair in _sent)
            if (pair.Value && _rows.TryGetValue(pair.Key, out var rows))
                foreach (var row in rows)
                    Pulse(row);
        _sent.Clear();
    }

    // Lit, then fading back to what the stylesheet says (sent again meanwhile: lit again).
    private static void Pulse(VisualElement row)
    {
        if (_fading.TryGetValue(row, out var running))
            running.Pause();
        double start = EditorApplication.timeSinceStartup;
        IVisualElementScheduledItem item = null;
        item = row.schedule.Execute(() =>
        {
            float t = (float)((EditorApplication.timeSinceStartup - start) / Seconds);
            if (t >= 1f)
            {
                row.style.backgroundColor = StyleKeyword.Null;
                item.Pause();
                _fading.Remove(row);
                return;
            }
            row.style.backgroundColor = Color.Lerp(Lit, new Color(Lit.r, Lit.g, Lit.b, 0f), t * t);
        }).Every(16);
        _fading[row] = item;
        row.style.backgroundColor = Lit;
    }
}
