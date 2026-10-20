using Remedy.Framework;
using System;
using System.Collections.Generic;
//using System.Linq;
using UnityEngine.UIElements;
using UnityEngine;
using UnityEditor;
using System.Linq;
using SchematicAssets;

[FieldRendererTarget(typeof(SignalContainerRendererAttribute))]
public class SignalRenderer : FieldRenderer
{
    private static List<SignalRenderer> _eventLinkWaitPool = new(); // Events waiting for the rest of the Component to finish displaying before attempting to draw
    private SignalContainerRendererAttribute _evAttr;
    private static Texture2D _oIcon;
    private static Texture2D _outputIcon => _oIcon ??= Resources.Load<Texture2D>("Icons/Output");
    private static Texture2D _iIcon;
    private static Texture2D _inputIcon => _iIcon ??= Resources.Load<Texture2D>("Icons/Input");

    // Reflection caches
    private static readonly Dictionary<Type, Type[]> _genericArgumentsCache = new();
    private static readonly Dictionary<Type, Type> _baseTypeCache = new();
    private static readonly Dictionary<Type, Type> _eventTypeForArgumentCache = new();
    private static readonly Dictionary<Type, bool> _isAssignableFromCache = new();
    private static readonly Dictionary<(Type, Type), bool> _typeAssignabilityCache = new();

    public SignalRenderer(SchematicGraphEditorWindow window, UnityEngine.Object obj, object parent, object target, FieldOrPropertyInfo fieldInfo, CustomFieldRendererAttribute attr, string path, List<Action> onModified) : base(window, obj, parent, target, fieldInfo, attr, path, onModified)
    {
        _evAttr = (SignalContainerRendererAttribute)attr;

        if (_evAttr == null)
            _evAttr = new SignalContainerRendererAttribute(typeof(SignalData), _field.Name, false);

        // SchematicEventLinkAttribute (the only reason to wait for the rest of the Component) no longer exists, so
        // every Signal container draws straight away; DrawDelayedEvents still flushes anything left in the pool.
        DrawEventContainer();
    }

    /// <summary>
    /// Cached wrapper for Type.GetGenericArguments()
    /// </summary>
    private static Type[] GetGenericArguments(Type type)
    {
        if (!_genericArgumentsCache.TryGetValue(type, out var args))
        {
            args = type.GetGenericArguments();
            _genericArgumentsCache[type] = args;
        }
        return args;
    }

    /// <summary>
    /// Cached wrapper for Type.BaseType
    /// </summary>
    private static Type GetBaseType(Type type)
    {
        if (!_baseTypeCache.TryGetValue(type, out var baseType))
        {
            baseType = type.BaseType;
            _baseTypeCache[type] = baseType;
        }
        return baseType;
    }

    /// <summary>
    /// Cached wrapper for Type.IsAssignableFrom()
    /// </summary>
    private static bool IsAssignableFrom(Type targetType, Type sourceType)
    {
        var key = (targetType, sourceType);
        if (!_typeAssignabilityCache.TryGetValue(key, out var result))
        {
            result = targetType.IsAssignableFrom(sourceType);
            _typeAssignabilityCache[key] = result;
        }
        return result;
    }

    /// <summary>
    /// Clears all reflection caches. Call this if types are reloaded or modified at runtime.
    /// </summary>
    public static void ClearReflectionCaches()
    {
        _genericArgumentsCache.Clear();
        _baseTypeCache.Clear();
        _eventTypeForArgumentCache.Clear();
        _isAssignableFromCache.Clear();
        _typeAssignabilityCache.Clear();
    }

    /// <summary>
    /// Draws the Events that were waiting for the rest of the Component to render before they themselves rendered.
    /// </summary>
    public static void DrawDelayedEvents()
    {
        foreach (var eventContainer in _eventLinkWaitPool)
        {
            eventContainer.DrawEventContainer();
        }
        _eventLinkWaitPool.Clear();
    }

    protected void DrawEventContainer()
    {
        // The Signal the field already points at, if it has one: the SignalData a field holds, or a Schematic Behavior's
        // Signal (its SignalHandle field's asset, kept in the Behavior's Metadata). Only fields without one get theirs
        // from a SignalHandler, as before.
        var behaviorMetadata = _object as BehaviorMetadata;
        var existingSignal = _value as SignalData;
        if (existingSignal == null && behaviorMetadata != null)
            existingSignal = behaviorMetadata.FindSignal(_field.Name)?.Asset;
        bool ownedByBehavior = behaviorMetadata != null && existingSignal != null;

        var handler = existingSignal == null ? new SignalHandler(GlobalObjectId.GetGlobalObjectIdSlow(_object), _path, _field) : null;
        var signalData = existingSignal != null ? existingSignal : handler.SignalAsset;

        if (existingSignal == null)
            AssignToEmptyField(signalData);

        // Signals named and typed by their owner (Remedy Input's): brought up to date before they're drawn.
        var owner = Owner();
        string ownerName = MemberValue(owner, _evAttr.NameFrom) as string;
        bool namedByOwner = !string.IsNullOrEmpty(_evAttr.NameFrom) && owner != null;
        if (namedByOwner && !string.IsNullOrWhiteSpace(ownerName) && signalData != null && signalData.Name != ownerName)
        {
            var renamed = signalData;
            string to = ownerName;
            EditorApplication.delayCall += () => { if (renamed != null) renamed.EditorRename(to); };
        }
        var decidedType = MemberValue(owner, _evAttr.ParameterTypeFrom) as Type;
        if (decidedType != null && signalData != null && signalData.SetSingleParameter(decidedType))
            EditorUtility.SetDirty(signalData);
        if (_evAttr.Outgoing && signalData != null)
            OutgoingSignals.Add(signalData);

        var column = new VisualElement
        {
            style =
            {
                flexDirection = FlexDirection.Column,

                borderBottomColor = Color.gray4,
                borderTopColor = Color.gray4,

                borderRightColor = Color.black,
                borderLeftColor = Color.black
            }
        };

        var row = new VisualElement
        {
            style =
            {
                flexDirection = FlexDirection.Row,
                borderBottomWidth = 0,
                paddingLeft = 0
            }
        };

        // Name field
        var nameField = new TextField { value = namedByOwner && !string.IsNullOrWhiteSpace(ownerName) ? ownerName : _field.Name, style = { flexGrow = 1, paddingLeft = 0 } };

        nameField.Children().ElementAt(0).AddToClassList("event-name");

        nameField.RegisterCallback<MouseEnterEvent>(_ => nameField.Children().ElementAt(0).AddToClassList("event-name-hover"));
        nameField.RegisterCallback<MouseLeaveEvent>(_ => nameField.Children().ElementAt(0).RemoveFromClassList("event-name-hover"));

        string oldName = nameField.value;
        string newName = nameField.value;

        nameField.RegisterValueChangedCallback(evt =>
        {
            newName = evt.newValue;
        });

        nameField.RegisterCallback<FocusInEvent>(evt =>
        {
            Undo.RecordObject(_window, "Edit Event Name");
            oldName = nameField.value;
            _window.Canvas.Reload();
        });

        nameField.RegisterCallback<FocusOutEvent>((EventCallback<FocusOutEvent>)(evt =>
        {
            Undo.RecordObject(_window, "Edit Event Name");
            if (namedByOwner)
            {
                // Renamed here, renamed on the owner too (the input it belongs to), and the other way around.
                newName = newName?.Trim();
                if (!string.IsNullOrEmpty(newName) && newName != oldName)
                {
                    var live = Owner() ?? owner;
                    if (_object != null)
                        Undo.RecordObject(_object, "Rename Signal");
                    SetMemberValue(live, _evAttr.NameFrom, newName);
                    if (_object != null)
                        EditorUtility.SetDirty(_object);
                    signalData.EditorRename(newName);
                    EditorApplication.delayCall += () => { if (_window != null) _window.RedrawIODock(true); };
                }
            }
            else if (handler != null)
                handler.ChangeName(oldName, newName);
            else if (!ownedByBehavior && oldName != newName && !string.IsNullOrWhiteSpace(newName))
            {
                Undo.RecordObject(signalData, "Edit Event Name");
                signalData.name = newName;
                signalData.Name = newName;
                EditorUtility.SetDirty(signalData);
            }
            _window.Canvas.Reload();
        }));

        // A Behavior's Signals are named by its code.
        nameField.isReadOnly = ownedByBehavior;

        // Global toggle
        var globalToggle = new Toggle { value = signalData.Global };

        globalToggle.RegisterValueChangedCallback(evt =>
        {
            Undo.RecordObject(_window, "Toggle Global");
            signalData.Global = evt.newValue;
            _window.Canvas.Reload();
        });

        bool signalType = _evAttr.Outgoing || _field.Attributes.Any(attr => attr.AttributeType == typeof(OutputSignalAttribute));

        // Drag Handle
        Texture2D myTexture = signalType ? myTexture = _outputIcon : myTexture = _inputIcon;

        Image imageElement = new Image();

        imageElement.style.cursor = new StyleCursor(UnityDefaultCursor.DefaultCursor(UnityDefaultCursor.CursorType.Link));
        imageElement.RegisterCallback<MouseDownEvent>(evt =>
        {
            DragAndDrop.PrepareStartDrag();
            DragAndDrop.objectReferences = new UnityEngine.Object[] { signalData };
            DragAndDrop.StartDrag("Drag ScriptableEvent");
            evt.StopPropagation();
        });

        imageElement.RegisterCallback<MouseEnterEvent>(evt =>
        {
            column.AddToClassList("event-container-hover");
        });

        imageElement.RegisterCallback<MouseLeaveEvent>(evt =>
        {
            column.RemoveFromClassList("event-container-hover");
        });

        imageElement.image = myTexture;
        imageElement.style.width = 32;
        imageElement.style.height = 24;
        imageElement.style.opacity = 0.75f;

        imageElement.tooltip = signalType ? "Output Signal" : "Input Signal";

        row.Add(nameField);
        row.Add(imageElement);

        column.AddToClassList("event-container");
        column.AddToClassList("event-container-" + (signalType ? "output" : "input"));

        var paramAttrs = _field.GetCustomAttributes().OfType<ParameterAttribute>();

        // Parameters declared in code ([Parameter]) replace the Signal's. A Behavior's Signals are kept in step with
        // its code by the Behavior Metadata, and a field that declares none keeps the parameters edited here.
        bool syncCodeParameters = !ownedByBehavior && paramAttrs.Any();

        if (syncCodeParameters)
            signalData.Parameters.Contents.Clear();

        foreach(var param in syncCodeParameters ? paramAttrs : Enumerable.Empty<ParameterAttribute>())
        {
            if(!signalData.Parameters.Contents.Any(item => item.Name == param.Name))
            {
                signalData.Parameters.AddParameter(param.Name, new Union(param.Type));
                var paramInSignal = signalData.Parameters.Contents.FirstOrDefault(item => item.Name == param.Name);
                paramInSignal.Value.Type = Union.ReverseTypeLookup[param.Type];
            }
        }
        foreach (var param in syncCodeParameters ? paramAttrs : Enumerable.Empty<ParameterAttribute>())
        {
            if (signalData.Parameters.Contents.Any(item => item.Name == param.Name))
            {
                var paramInSignal = signalData.Parameters.Contents.FirstOrDefault(item => item.Name == param.Name);
                paramInSignal.Value.Type = Union.ReverseTypeLookup[param.Type];
            }
        }

        var paramList = new LazyListFoldout(collection: signalData.Parameters.Contents,
                                            className: "event-parameters",
                                            title: "Parameters",
                                            onItemAdded: (object item) => 
                                            { 
                                                signalData.Parameters.Contents.Add((SignalData.Parameter)item);
                                                signalData.RuntimeArgs = new Union[signalData.Parameters.Contents.Count];
                                            },
                                            onItemRemoved: (object item) => 
                                            { 
                                                signalData.Parameters.Contents.Remove((SignalData.Parameter)item);
                                                signalData.RuntimeArgs = new Union[signalData.Parameters.Contents.Count];
                                            },
                                            createItemInstance: (Action<object> onFinished) =>
                                            {
                                                var value = new SignalData.Parameter();
                                                onFinished?.Invoke(value);
                                            },
                                            createItemContent: (int index, object value) =>
                                            {
                                                var param = signalData.Parameters[index];
                                                var paramType = param.Type;

                                                var typePopup = new PopupField<Type>(
                                                    Union.TypeLookup.Values.Where(type => type != null).ToList(),
                                                    Union.TypeLookup[param.Type],
                                                    t => t?.Name ?? "???",
                                                    t => t?.Name ?? "???"
                                                );

                                                typePopup.SetEnabled(_evAttr.ParamTypeModifiable && decidedType == null);
                                                if (decidedType != null)
                                                    typePopup.tooltip = "Set by the Input Action's control type.";

                                                var nameField = new TextField { value = ((SignalData.Parameter)value).Name , style = { flexGrow = 1, paddingLeft = 0 } };

                                                nameField.Children().ElementAt(0).AddToClassList("event-name");

                                                nameField.RegisterCallback<MouseEnterEvent>(_ => nameField.Children().ElementAt(0).AddToClassList("event-name-hover"));
                                                nameField.RegisterCallback<MouseLeaveEvent>(_ => nameField.Children().ElementAt(0).RemoveFromClassList("event-name-hover"));

                                                string oldName = nameField.value;
                                                string newName = nameField.value;
                                                                
                                                nameField.RegisterValueChangedCallback(evt =>
                                                {
                                                    newName = evt.newValue;
                                                });

                                                nameField.SetEnabled(_evAttr.ParamNameModifiable);

                                                var paramRow = new VisualElement
                                                {
                                                    style =
                                                    {
                                                        flexDirection = FlexDirection.Row,
                                                        borderBottomWidth = 0,
                                                        paddingLeft = 0
                                                    }
                                                };

                                                paramRow.AddToClassList("event-parameter-container");
                                                paramRow.RegisterCallback<MouseEnterEvent>(_ => nameField.Children().ElementAt(0).AddToClassList("event-parameter-container-hover"));
                                                paramRow.RegisterCallback<MouseLeaveEvent>(_ => nameField.Children().ElementAt(0).RemoveFromClassList("event-parameter-container-hover"));

                                                paramRow.Add(typePopup);
                                                paramRow.Add(nameField);

                                                var typeDef = SchematicEditorData.EditorTypeSettings.FirstOrDefault(item => Type.GetType(item.Type) == typePopup.value);
                                                if (typeDef != null)
                                                    paramRow.style.backgroundColor = typeDef.ColorInEditor;
                                                else
                                                {
                                                    Debug.LogWarning("Type Definition does not exist for: " + typePopup.value);
                                                }

                                                return paramRow;
                                            },
                                            getItemName: (object item) => { return ((SignalData.Parameter)item).Name; },
                                            contentModifiable: _evAttr.ParametersEditable && decidedType == null,
                                            startExpanded: true
                                            );

        signalData.RuntimeArgs = new Union[signalData.Parameters.Contents.Count];

        column.Add(row);
        column.Add(paramList);

        Add(column);
    }


    /// <summary>
    /// Signals the code sends and graphs react to (<see cref="SignalContainerRendererAttribute.Outgoing"/>), so a graph
    /// they're dropped into makes On Received for them without asking.
    /// </summary>
    internal static readonly HashSet<SignalData> OutgoingSignals = new();

    // The object holding the Signal field right now (an input of Remedy Input's), found by its path when possible.
    private object Owner()
    {
        object resolved = null;
        try { resolved = string.IsNullOrEmpty(_path) ? _object : ManagedPath.Resolve(_object, _path); }
        catch { }
        if (resolved != null && Member(resolved.GetType(), _field.Name) != null)
            return resolved;
        return _parent;
    }

    private static System.Reflection.MemberInfo Member(Type type, string name)
    {
        if (type == null || string.IsNullOrEmpty(name))
            return null;
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
        return (System.Reflection.MemberInfo)type.GetField(name, flags) ?? type.GetProperty(name, flags);
    }

    private static object MemberValue(object owner, string name)
    {
        switch (Member(owner?.GetType(), name))
        {
            case System.Reflection.FieldInfo field: return field.GetValue(owner);
            case System.Reflection.PropertyInfo property when property.CanRead:
                try { return property.GetValue(owner); } catch { return null; }
            default: return null;
        }
    }

    private static void SetMemberValue(object owner, string name, object value)
    {
        switch (Member(owner?.GetType(), name))
        {
            case System.Reflection.FieldInfo field: field.SetValue(owner, value); break;
            case System.Reflection.PropertyInfo property when property.CanWrite: property.SetValue(owner, value); break;
        }
    }

    /// <summary>
    /// A <see cref="SignalData"/> field that's still empty is given the Signal made for it, so the code that sends it
    /// (e.g. Remedy Input's Output) and the graphs that use it share the same one.
    /// </summary>
    private void AssignToEmptyField(SignalData signal)
    {
        if (signal == null || _object == null || !typeof(SignalData).IsAssignableFrom(_field.MemberType))
            return;

        var property = DockSerializedObjects.FindProperty(_object, string.IsNullOrEmpty(_path) ? _field.Name : _path + "." + _field.Name);
        if (property == null || property.propertyType != SerializedPropertyType.ObjectReference || property.objectReferenceValue != null)
            return;
        var serializedObject = property.serializedObject;

        property.objectReferenceValue = signal;
        serializedObject.ApplyModifiedPropertiesWithoutUndo();
        _value = signal;
    }

    protected IMGUIContainer DrawGUIContainer()
    {
        SerializedObject seriializedObject = new SerializedObject(_object);

        var valPath = _path + "." + _field.Name + "." + nameof(SignalData);
        var property = seriializedObject.FindProperty(valPath);

        var imguiContainerContainer = new VisualElement();

        var imguiContainer = new IMGUIContainer()
        {
            style =
            {
                flexGrow = 0,
                flexShrink = 0
            }
        };

        imguiContainer.onGUIHandler = () =>
        {
            seriializedObject.Update();
            EditorGUILayout.PropertyField(property);
            seriializedObject.ApplyModifiedProperties();

            EditorApplication.QueuePlayerLoopUpdate();
            imguiContainer.MarkDirtyRepaint();
        };

        EditorApplication.update += () =>
        {
            if (imguiContainer == null) return;
            imguiContainer.MarkDirtyRepaint();
        };

        imguiContainer.focusable = true;
        imguiContainer.tabIndex = 0;

        return imguiContainer;
    }
}