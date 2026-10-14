using Remedy.Framework;
using Remedy.Schematics;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Draws a Signal field (one marked <see cref="SignalContainerRendererAttribute"/>, like each Remedy Input's
/// Output) in the IO Dock: its icon (drag it into a graph), its name (rename it in place; applies on Enter or when
/// you click away) and its parameters, each with a name and a type picked from a dropdown, plus "Add Parameter…".
/// The Signal asset is made the first time the field is drawn, saved inside the asset that owns the field, and
/// assigned to the field (so the code that sends it, and graphs that react to it, use the same one).
/// Parameters declared in code with <c>[Parameter]</c> on the field are fixed and kept in step with the code.
/// </summary>
[FieldRendererTarget(typeof(SignalContainerRendererAttribute))]
public class SignalRenderer : FieldRenderer
{
    private SignalContainerRendererAttribute _signalAttr;
    private string _fieldPath;

    public SignalRenderer(SchematicGraphEditorWindow window, UnityEngine.Object obj, object parent, object target, FieldOrPropertyInfo fieldInfo, CustomFieldRendererAttribute attr, string path, List<Action> onModified) : base(window, obj, parent, target, fieldInfo, attr, path, onModified)
    {
    }

    /// <summary>Kept for older callers; Signal containers now draw straight away.</summary>
    public static void DrawDelayedEvents() { }

    protected override void Redraw()
    {
        _signalAttr = _attr as SignalContainerRendererAttribute ?? new SignalContainerRendererAttribute(typeof(SignalData), _field.Name, false);
        _fieldPath = ManagedPath.Combine(_path, _field.Name);
        Clear();

        SignalData signal;
        try { signal = EnsureSignal(); }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            signal = null;
        }
        if (signal == null)
        {
            var missing = new Label("This Signal can't be created here: the object that owns it isn't saved as an asset.");
            missing.style.whiteSpace = WhiteSpace.Normal;
            missing.style.fontSize = 10;
            Add(missing);
            return;
        }

        SyncCodeParameters(signal);
        Build(signal);
    }

    #region The Signal asset

    // The field's Signal: the one it holds, an old one made for it by earlier versions, or a new one saved inside the
    // owning asset. Assigned through the shared SerializedObject so Undo and the other dock fields see it.
    private SignalData EnsureSignal()
    {
        var serializedObject = DockSerializedObjects.Get(_object);
        if (serializedObject == null)
            return null;
        serializedObject.UpdateIfRequiredOrScript();
        var property = serializedObject.FindProperty(_fieldPath);
        if (property == null || property.propertyType != SerializedPropertyType.ObjectReference)
            return null;
        if (property.objectReferenceValue is SignalData existing)
            return existing;
        if (!EditorUtility.IsPersistent(_object))
            return null;

        var signal = FindLegacySignal() ?? CreateSignal();
        if (signal == null)
            return null;
        property.objectReferenceValue = signal;
        serializedObject.ApplyModifiedPropertiesWithoutUndo();
        AssetDatabase.SaveAssetIfDirty(_object);
        return signal;
    }

    // Earlier versions made the Signal as its own file under Assets/_Schematics/<object id>/<field path>/ and never
    // assigned it to the field; graphs may already use it, so it's adopted rather than replaced.
    private SignalData FindLegacySignal()
    {
        string folder = "Assets/_Schematics/" + GlobalObjectId.GetGlobalObjectIdSlow(_object) + "/"
                      + _fieldPath.Replace(".Array.data[", "[").Replace('.', '/');
        if (!AssetDatabase.IsValidFolder(folder))
            return null;
        foreach (var guid in AssetDatabase.FindAssets("t:" + nameof(SignalData), new[] { folder }))
            if (AssetDatabase.LoadAssetAtPath<SignalData>(AssetDatabase.GUIDToAssetPath(guid)) is { } signal)
                return signal;
        return null;
    }

    private SignalData CreateSignal()
    {
        var signal = ScriptableObject.CreateInstance<SignalData>();
        string name = DefaultName();
        signal.name = name;
        signal.Name = name;
        AssetDatabase.AddObjectToAsset(signal, _object);
        EditorUtility.SetDirty(_object);
        return signal;
    }

    // The attribute's DefaultName is a member of the field's owner that returns the name (Remedy Input: "Input Jump"),
    // or the name itself.
    private string DefaultName()
    {
        string name = null;
        var owner = ManagedPath.Resolve(_object, _path);
        if (owner != null && !string.IsNullOrEmpty(_signalAttr.DefaultName))
        {
            try { name = owner.GetType().GetFieldOrProperty(_signalAttr.DefaultName)?.GetValue(owner) as string; }
            catch (ArgumentException) { name = _signalAttr.DefaultName; }
        }
        if (string.IsNullOrWhiteSpace(name))
            name = string.IsNullOrEmpty(_signalAttr.DefaultName) ? ObjectNames.NicifyVariableName(_field.Name) : _signalAttr.DefaultName;
        return name.Trim();
    }

    private static void Rename(SignalData signal, string name)
    {
        if (signal == null || string.IsNullOrWhiteSpace(name) || signal.name == name)
            return;
        name = name.Trim();
        string path = AssetDatabase.GetAssetPath(signal);
        if (AssetDatabase.IsMainAsset(signal))
        {
            // Its own file: renaming the file renames the asset.
            string error = AssetDatabase.RenameAsset(path, name);
            if (!string.IsNullOrEmpty(error))
            {
                Debug.LogWarning("Couldn't rename the Signal: " + error);
                return;
            }
            Undo.RecordObject(signal, "Rename Signal");
            signal.Name = name;
        }
        else
        {
            Undo.RecordObject(signal, "Rename Signal");
            signal.name = name;
            signal.Name = name;
        }
        EditorUtility.SetDirty(signal);
        AssetDatabase.SaveAssetIfDirty(signal);
        SignalPortSync.RenameNodes(signal, name);
    }

    #endregion

    #region Parameters

    private List<ParameterAttribute> CodeParameters()
        => _field.GetCustomAttributes().OfType<ParameterAttribute>().ToList();

    // Parameters declared in code win: the asset is brought in line with them (only when it differs, so drawing
    // never touches the asset otherwise).
    private void SyncCodeParameters(SignalData signal)
    {
        var declared = CodeParameters();
        if (declared.Count == 0)
            return;
        List<(string name, Type type)> wanted = declared.Select(p => (name: p.Name, type: p.Type)).ToList();
        var current = SignalPortSync.AssetParameters(signal);
        if (wanted.Count == current.Count && !wanted.Where((p, i) => p.name != current[i].name || p.type != current[i].type).Any())
            return;
        SignalPortSync.SetAssetParameters(signal, wanted);
    }

    #endregion

    private void Build(SignalData signal)
    {
        bool outgoing = _signalAttr.Outgoing || _field.Attributes.Any(a => a.AttributeType == typeof(OutputSignalAttribute));
        bool incoming = !outgoing && _field.Attributes.Any(a => a.AttributeType == typeof(InputSignalAttribute));

        var card = new VisualElement();
        card.AddToClassList("event-container");
        card.AddToClassList(outgoing ? "event-container-output" : "event-container-input");
        card.style.marginTop = 2;
        card.style.marginBottom = 2;

        var row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.alignItems = Align.Center;

        // The icon is the handle: drag it into a graph (On Received for a Signal the code sends, Send for one it hears).
        var item = new DockItem
        {
            Kind = DockItem.ItemKind.Signal, Signal = signal, Name = signal.name,
            Direction = outgoing ? SignalDirection.Output : incoming ? SignalDirection.Input : SignalDirection.Unspecified,
        };
        var icon = new Image { image = outgoing ? DockRows.OutputIcon : DockRows.InputIcon };
        icon.style.width = 16;
        icon.style.height = 16;
        icon.style.marginRight = 4;
        icon.style.flexShrink = 0;
        icon.style.cursor = new StyleCursor(UnityDefaultCursor.DefaultCursor(UnityDefaultCursor.CursorType.Link));
        icon.tooltip = outgoing ? "Output Signal: sent from code. Drag it into a graph to react to it (On Received); click it to see it in the Inspector."
                                : "Signal: drag it into a graph to use it; click it to see it in the Inspector.";
        DockRows.MakeDraggable(icon, item, () =>
        {
            _window?.FocusNodes(item);
            ScopeItemInspector.Show(item);
        });
        row.Add(icon);

        var nameField = new TextField { value = signal.name, isDelayed = true, tooltip = "The Signal's name. Press Enter or click away to apply." };
        nameField.AddToClassList("event-name");
        nameField.style.flexGrow = 1;
        nameField.style.flexShrink = 1;
        nameField.style.minWidth = 40;
        nameField.RegisterValueChangedCallback(evt =>
        {
            string name = evt.newValue?.Trim();
            if (string.IsNullOrEmpty(name))
            {
                nameField.SetValueWithoutNotify(signal.name);
                return;
            }
            Rename(signal, name);
            nameField.SetValueWithoutNotify(signal.name);
            item.Name = signal.name;
        });
        row.Add(nameField);
        card.Add(row);

        bool fixedByCode = CodeParameters().Count > 0;
        var parameters = new SignalParameterList(
            () => SignalPortSync.AssetParameters(signal),
            (list, from, to) => SignalPortSync.SetAssetParameters(signal, list, from, to),
            SignalParameterList.UnionTypes,
            editable: !fixedByCode && _signalAttr.ParametersEditable,
            namesEditable: !fixedByCode && (_signalAttr.ParamNameModifiable || _signalAttr.ParametersEditable),
            typesEditable: !fixedByCode && (_signalAttr.ParamTypeModifiable || _signalAttr.ParametersEditable),
            max: _signalAttr.MaxParameters);
        card.Add(parameters);

        Add(card);
    }
}
