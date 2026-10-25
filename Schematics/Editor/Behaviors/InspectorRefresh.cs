using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// How often the Schematic Behaviors Inspector, the Prefab Hierarchy and the IO Dock redraw by themselves to show
/// values that change (live ones in Play Mode): at most Refresh Rate times a second, all together, rather than every
/// editor frame each. Anything you touch still redraws at once. Set in Preferences > Schematics > Inspector, or from
/// the Schematic Behaviors header in Play Mode.
/// </summary>
[InitializeOnLoad]
public static class InspectorRefresh
{
    private const string RateKey = "Remedy.Schematics.InspectorRefreshRate";

    /// <summary>The choices offered (redraws a second; 0 = only when you touch it).</summary>
    public static readonly int[] Rates = { 0, 1, 2, 5, 10, 20, 30, 60 };
    private static readonly GUIContent[] RateLabels =
    {
        new("Off (when touched)"), new("1 / s"), new("2 / s"), new("5 / s"), new("10 / s"), new("20 / s"), new("30 / s"), new("60 / s"),
    };

    private static int? _rate;
    private static double _next;
    private static readonly HashSet<Editor> _editors = new();
    private static readonly HashSet<VisualElement> _elements = new();

    static InspectorRefresh() => EditorApplication.update += Tick;

    /// <summary>Redraws a second (0: only when touched). Kept between sessions.</summary>
    public static int RatePerSecond
    {
        get => _rate ??= EditorPrefs.GetInt(RateKey, 10);
        set
        {
            _rate = Mathf.Max(0, value);
            EditorPrefs.SetInt(RateKey, _rate.Value);
        }
    }

    /// <summary>Asks for an Inspector to be redrawn at the next refresh (ask again each time it draws to keep it live).</summary>
    public static void Request(Editor editor)
    {
        if (editor != null)
            _editors.Add(editor);
    }

    /// <summary>Asks for a UI Toolkit element (an IMGUIContainer) to be redrawn at the next refresh.</summary>
    public static void Request(VisualElement element)
    {
        if (element != null)
            _elements.Add(element);
    }

    private static void Tick()
    {
        if (_editors.Count == 0 && _elements.Count == 0)
            return;
        int rate = RatePerSecond;
        if (rate <= 0)
        {
            _editors.Clear();
            _elements.Clear();
            return;
        }
        double now = EditorApplication.timeSinceStartup;
        if (now < _next)
            return;
        _next = now + 1.0 / rate;
        foreach (var editor in _editors)
            if (editor != null)
                editor.Repaint();
        foreach (var element in _elements)
            if (element?.panel != null)
                element.MarkDirtyRepaint();
        _editors.Clear();
        _elements.Clear();
    }

    /// <summary>A compact Refresh Rate popup (for headers).</summary>
    public static void DrawRatePopup(params GUILayoutOption[] options)
    {
        int index = System.Array.IndexOf(Rates, RatePerSecond);
        if (index < 0) index = 4;
        int picked = EditorGUILayout.Popup(GUIContent.none, index, RateLabels, EditorStyles.miniPullDown, options);
        if (picked != index)
            RatePerSecond = Rates[picked];
    }

    [SettingsProvider]
    private static SettingsProvider CreateProvider() => new("Preferences/Schematics/Inspector", SettingsScope.User)
    {
        keywords = new HashSet<string> { "inspector", "refresh", "repaint", "lag", "play mode", "rate", "behaviors", "variables" },
        guiHandler = _ =>
        {
            EditorGUIUtility.labelWidth = 200;
            EditorGUILayout.HelpBox("How often the Schematic Behaviors Inspector, the Prefab Hierarchy and the IO Dock redraw by themselves to show "
                + "changing values (live ones in Play Mode). Lower it if keeping them open slows the editor down; anything you click or type in "
                + "still updates at once.", MessageType.None);
            int index = System.Array.IndexOf(Rates, RatePerSecond);
            if (index < 0) index = 4;
            int picked = EditorGUILayout.Popup(new GUIContent("Refresh Rate", "Redraws a second. Off: only when you touch it."), index, RateLabels);
            if (picked != index)
                RatePerSecond = Rates[picked];
            EditorGUILayout.Space(6);
            SchematicBehaviorsHeaderGUI.ShowInInspector = EditorGUILayout.Toggle(new GUIContent("Schematic Behaviors In Inspector",
                "Show a Schematic Object's Behaviors (Variables, Signals, Properties) in the Inspector too. Off: the IO Dock has them."),
                SchematicBehaviorsHeaderGUI.ShowInInspector);
        },
    };
}
