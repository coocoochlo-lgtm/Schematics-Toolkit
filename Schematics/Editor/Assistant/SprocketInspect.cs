using BlueGraph;
using BlueGraph.Editor;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace Remedy.Schematics.Assistant
{
    /// <summary>Something Sprocket was dropped on: what to call it, what to say, and where it is on screen.</summary>
    internal sealed class InspectTarget
    {
        public string Title;
        public string Text;
        public Rect ScreenRect;
        public EditorWindow Window;
    }

    /// <summary>
    /// "What is this?": finds what's under a screen point (a graph node, an IO Dock row, an object in the Scene view,
    /// or for windows without a way to point at items, what's selected) and looks it up in the documentation.
    /// </summary>
    internal static class SprocketInspect
    {
        #region Finding what's there

        /// <summary>
        /// What's at a screen point in a window. The Hierarchy and Project window answer a moment later, so this calls
        /// back. When nothing in particular is there, it's the window itself.
        /// </summary>
        public static void At(EditorWindow window, Vector2 screen, Action<InspectTarget> done)
        {
            void Finish(InspectTarget target) => done(target ?? ForWindow(window));
            switch (window)
            {
                case null:
                    done(null);
                    return;
                case SceneView sceneView:
                {
                    var picked = PickInScene(sceneView, screen);
                    Finish(picked != null ? Placed(ForGameObject(picked), window, screen) : null);
                    return;
                }
                case SchematicGraphEditorWindow graph:
                    Finish(InGraph(graph, screen));
                    return;
            }
            switch (window.GetType().Name)
            {
                case "HierarchyWindow":
                {
                    var picked = InNewHierarchy(window, screen);
                    Finish(picked != null ? Placed(ForGameObject(picked), window, screen) : null);
                    return;
                }
                case "SceneHierarchyWindow":
                    Probe(window, screen, picked => Finish(picked is GameObject gameObject ? Placed(ForGameObject(gameObject), window, screen) : null));
                    return;
                case "ProjectBrowser":
                    Probe(window, screen, asset => Finish(asset != null ? Placed(ForAsset(asset), window, screen) : null));
                    return;
                case "InspectorWindow":
                    Finish(InInspector(window, screen));
                    return;
            }
            Finish(null);
        }

        /// <summary>Just the name of what's under the pointer, for the "What is this?" tag while dragging.</summary>
        public static void Peek(EditorWindow window, Vector2 screen, Action<string> done)
        {
            switch (window)
            {
                case null:
                    done(null);
                    return;
                case SceneView sceneView:
                    done(PickInScene(sceneView, screen)?.name);
                    return;
                case SchematicGraphEditorWindow graph:
                    done(InGraph(graph, screen)?.Title);
                    return;
            }
            switch (window.GetType().Name)
            {
                case "HierarchyWindow":
                    done(InNewHierarchy(window, screen)?.name);
                    return;
                case "SceneHierarchyWindow":
                case "ProjectBrowser":
                    Probe(window, screen, picked => done(picked?.name));
                    return;
                case "InspectorWindow":
                    done(InInspector(window, screen)?.Title);
                    return;
            }
            done(null);
        }

        private static InspectTarget Placed(InspectTarget target, EditorWindow window, Vector2 screen)
        {
            target.Window = window;
            target.ScreenRect = new Rect(screen - new Vector2(30, 12), new Vector2(60, 24));
            return target;
        }

        #endregion

        #region Screen and panel points

        // A window's UI starts at the top of its content (below the tab), where window.position starts. In some windows
        // (the Scene view in Prefab Mode) the root sits lower, under a toolbar, so points go through the outermost
        // element instead.
        private static VisualElement Outermost(EditorWindow window)
        {
            var element = window.rootVisualElement;
            var top = element?.panel?.visualTree;
            while (element?.parent != null && element.parent != top)
                element = element.parent;
            return element;
        }

        /// <summary>A screen point in a window's panel space.</summary>
        public static Vector2 ScreenToPanel(EditorWindow window, Vector2 screen)
        {
            var outer = Outermost(window);
            return outer != null ? outer.LocalToWorld(screen - window.position.position) : screen - window.position.position;
        }

        /// <summary>A point in a window's panel space on the screen.</summary>
        public static Vector2 PanelToScreen(EditorWindow window, Vector2 panelPoint)
        {
            var outer = Outermost(window);
            return window.position.position + (outer != null ? outer.WorldToLocal(panelPoint) : panelPoint);
        }

        private static readonly List<VisualElement> _picks = new();

        /// <summary>The topmost UI element at a screen point, looking past Sprocket himself.</summary>
        private static VisualElement ElementAt(EditorWindow window, Vector2 screen)
        {
            var panel = window.rootVisualElement?.panel;
            if (panel == null)
                return null;
            _picks.Clear();
            panel.PickAll(ScreenToPanel(window, screen), _picks);
            foreach (var element in _picks)
                if (!IsSprocket(element))
                    return element;
            return null;
        }

        private static bool IsSprocket(VisualElement element)
        {
            for (; element != null; element = element.parent)
                if (element is AssistantOverlay)
                    return true;
            return false;
        }

        #endregion

        #region Schematic Graph

        private static InspectTarget InGraph(SchematicGraphEditorWindow window, Vector2 screen)
        {
            for (var element = ElementAt(window, screen); element != null; element = element.parent)
            {
                // IO Dock rows carry their item.
                if (element.userData is DockItem item)
                    return Target(window, element, ForDockItem(item));
                if (element is NodeView view && view.Target != null)
                    return Target(window, view, ForNode(view.Target));
            }
            return null;
        }

        #endregion

        #region Scene view

        // Unity's own picking only works from inside the Scene view's GUI, and not while it's drawing (that's what
        // flashed the view and logged rendering errors). So Sprocket looks for himself: a ray from the Scene camera
        // through the point, against the objects' meshes (and bounds), plus the icons of objects that are drawn as
        // icons (lights, cameras, sounds…), which sit on top.
        private static readonly PropertyInfo _cameraElement =
            typeof(SceneView).GetProperty("cameraViewVisualElement", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        private static readonly MethodInfo _intersectRayMesh =
            typeof(HandleUtility).GetMethod("IntersectRayMesh", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public,
                null, new[] { typeof(Ray), typeof(Mesh), typeof(Matrix4x4), typeof(RaycastHit).MakeByRefType() }, null);
        private static IEnumerable<Component> IconComponents(StageHandle stage)
        {
            foreach (var component in stage.FindComponentsOfType<Light>()) yield return component;
            foreach (var component in stage.FindComponentsOfType<Camera>()) yield return component;
            foreach (var component in stage.FindComponentsOfType<AudioSource>()) yield return component;
            foreach (var component in stage.FindComponentsOfType<ReflectionProbe>()) yield return component;
            foreach (var component in stage.FindComponentsOfType<LightProbeGroup>()) yield return component;
            foreach (var component in stage.FindComponentsOfType<SchematicObject>()) yield return component;
        }
        private const float IconRadius = 14f;

        /// <summary>The object at a screen point in a Scene view (null if none).</summary>
        public static GameObject PickInScene(SceneView view, Vector2 screen)
        {
            var camera = view != null ? view.camera : null;
            if (camera == null)
                return null;

            // The point inside the camera's rectangle, 0..1 from the bottom left.
            Rect viewport;
            Vector2 local;
            if (_cameraElement?.GetValue(view) is VisualElement cameraView && cameraView.panel != null)
            {
                var bounds = cameraView.worldBound;
                viewport = new Rect(0, 0, bounds.width, bounds.height);
                local = ScreenToPanel(view, screen) - bounds.position;
            }
            else
            {
                viewport = view.cameraViewport;
                local = screen - view.position.position - new Vector2(0, view.position.height - viewport.height);
            }
            if (viewport.width < 1f || viewport.height < 1f || !viewport.Contains(local))
                return null;
            var pixels = camera.pixelRect;
            var pixel = new Vector2(pixels.x + local.x / viewport.width * pixels.width, pixels.y + (1f - local.y / viewport.height) * pixels.height);
            float pointsPerPixel = viewport.width / Mathf.Max(1f, pixels.width);
            var ray = camera.ScreenPointToRay(pixel);

            var stage = StageUtility.GetCurrentStageHandle();
            var visibility = SceneVisibilityManager.instance;
            bool Pickable(GameObject gameObject) => gameObject.activeInHierarchy && !visibility.IsHidden(gameObject) && !visibility.IsPickingDisabled(gameObject);

            // Icons first: they're drawn over everything.
            GameObject icon = null;
            float iconDepth = float.MaxValue;
            if (view.drawGizmos)
                foreach (var component in IconComponents(stage))
                    {
                        if (component == null || component.GetComponent<Renderer>() != null || !Pickable(component.gameObject))
                            continue;
                        var at = camera.WorldToScreenPoint(component.transform.position);
                        if (at.z <= camera.nearClipPlane || at.z >= iconDepth)
                            continue;
                        if (Vector2.Distance(new Vector2(at.x, at.y), pixel) * pointsPerPixel <= IconRadius)
                        {
                            icon = component.gameObject;
                            iconDepth = at.z;
                        }
                    }
            if (icon != null)
                return icon;

            // Then surfaces: renderers whose bounds the ray crosses, nearest first, checked against their meshes.
            var candidates = new List<(Renderer renderer, float distance)>();
            foreach (var renderer in stage.FindComponentsOfType<Renderer>())
            {
                if (renderer == null || !renderer.enabled || renderer is ParticleSystemRenderer || !Pickable(renderer.gameObject))
                    continue;
                if (renderer.bounds.IntersectRay(ray, out float distance))
                    candidates.Add((renderer, distance));
            }
            candidates.Sort((a, b) => a.distance.CompareTo(b.distance));

            GameObject best = null;
            float bestDistance = float.MaxValue;
            foreach (var (renderer, boundsDistance) in candidates)
            {
                if (boundsDistance >= bestDistance)
                    break;
                float? hit = HitDistance(renderer, ray, boundsDistance);
                if (hit.HasValue && hit.Value < bestDistance)
                {
                    best = renderer.gameObject;
                    bestDistance = hit.Value;
                }
            }

            // Terrain and other colliders without a renderer.
            var physics = PhysicsSceneOf(stage);
            if (physics.IsValid() && physics.Raycast(ray.origin, ray.direction, out var physicsHit, bestDistance)
                && physicsHit.collider != null && Pickable(physicsHit.collider.gameObject))
                best = physicsHit.collider.gameObject;
            return best;
        }

        private static PhysicsScene PhysicsSceneOf(StageHandle stage)
        {
            var prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
            return prefabStage != null && stage == prefabStage.stageHandle ? prefabStage.scene.GetPhysicsScene() : Physics.defaultPhysicsScene;
        }

        // Where the ray meets the renderer's mesh; just its bounds for things that aren't plain meshes.
        private static float? HitDistance(Renderer renderer, Ray ray, float boundsDistance)
        {
            Mesh mesh = renderer switch
            {
                MeshRenderer => renderer.GetComponent<MeshFilter>() is { } filter ? filter.sharedMesh : null,
                SkinnedMeshRenderer skinned => skinned.sharedMesh,
                _ => null,
            };
            if (mesh == null || _intersectRayMesh == null || renderer is SkinnedMeshRenderer)
                return boundsDistance;
            try
            {
                var args = new object[] { ray, mesh, renderer.transform.localToWorldMatrix, null };
                return (bool)_intersectRayMesh.Invoke(null, args) ? ((RaycastHit)args[3]).distance : (float?)null;
            }
            catch
            {
                return boundsDistance;
            }
        }

        #endregion

        #region Hierarchy, Project and Inspector

        // Unity 6's Hierarchy window is made of UI elements: each row is a HierarchyViewItem whose handler knows its
        // GameObject. Read by reflection, so older editors (without it) are fine.
        private static GameObject InNewHierarchy(EditorWindow window, Vector2 screen)
        {
            for (var element = ElementAt(window, screen); element != null; element = element.parent)
            {
                var type = element.GetType();
                if (type.Name != "HierarchyViewItem")
                    continue;
                try
                {
                    var handler = type.GetProperty("Handler")?.GetValue(element);
                    var node = type.GetField("m_Node", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(element);
                    var getGameObject = handler?.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public).FirstOrDefault(m => m.Name == "GetGameObject" && m.GetParameters().Length == 1);
                    return node != null && getGameObject != null ? getGameObject.Invoke(handler, new[] { node }) as GameObject : null;
                }
                catch
                {
                    return null;
                }
            }
            return null;
        }

        // The Inspector: the component (or asset) under the pointer, and the setting under it if there is one.
        private static InspectTarget InInspector(EditorWindow window, Vector2 screen)
        {
            string property = null;
            for (var element = ElementAt(window, screen); element != null; element = element.parent)
            {
                if (property == null && element is IBindable bindable && !string.IsNullOrEmpty(bindable.bindingPath))
                    property = bindable.bindingPath;
                // Each component's block (an EditorElement) knows its Editor.
                var editorProperty = element.GetType().GetProperty("editor", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (editorProperty == null || !typeof(Editor).IsAssignableFrom(editorProperty.PropertyType))
                    continue;
                if (!(editorProperty.GetValue(element) is Editor editor) || editor.target == null)
                    continue;
                var description = editor.target switch
                {
                    GameObject gameObject => ForGameObject(gameObject),
                    SchematicObject schematic => ForGameObject(schematic.gameObject),
                    Component component => ForComponent(component),
                    var asset => ForAsset(asset),
                };
                if (property != null && FieldText(editor.target.GetType(), property) is { } field)
                    description.Text += "\n" + field;
                return Target(window, element, new Description { Title = description.Title, Text = description.Text });
            }
            var selected = Selection.activeObject;
            if (selected == null)
                return null;
            var fallback = selected is GameObject go ? ForGameObject(go) : ForAsset(selected);
            fallback.Window = window;
            fallback.ScreenRect = window.position;
            return fallback;
        }

        // A setting's own words: its [SchematicDoc], else its Tooltip.
        private static string FieldText(Type type, string bindingPath)
        {
            string name = bindingPath.Split('.')[0];
            FieldInfo field = null;
            for (var t = type; t != null && field == null; t = t.BaseType)
                field = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (field == null)
                return null;
            string about = SchematicDocHeader.Of(field)?.description ?? field.GetCustomAttribute<TooltipAttribute>()?.tooltip;
            return about != null ? $"<b>{Nicify(name)}:</b> {about}" : null;
        }

        // The Hierarchy (the classic one) and the Project window only say what's under the pointer to a drag. So
        // Sprocket asks with a pretend drag: a single "drag hover" event at the point, answered through Unity's
        // public drop handlers, then an exit so the windows forget it. Nothing is dropped and nothing changes. It's
        // sent once the current event is done (sending it from inside another window's event upsets the GUI).
        private const string ProbeKey = "Sprocket.Probe";
        private static bool _probing;
        private static UnityEngine.Object _probed;

        [InitializeOnLoadMethod]
        private static void RegisterProbes()
        {
            DragAndDrop.AddDropHandlerV2(OnHierarchyProbe);
            DragAndDrop.AddDropHandlerV2(OnProjectProbe);
        }

        private static DragAndDropVisualMode OnHierarchyProbe(EntityId target, HierarchyDropFlags flags, Transform parent, bool perform)
        {
            if (!_probing || DragAndDrop.GetGenericData(ProbeKey) == null)
                return DragAndDropVisualMode.None;
            _probed = EditorUtility.EntityIdToObject(target);
            return DragAndDropVisualMode.Link;
        }

        private static DragAndDropVisualMode OnProjectProbe(EntityId dragged, string path, bool perform)
        {
            if (!_probing || DragAndDrop.GetGenericData(ProbeKey) == null)
                return DragAndDropVisualMode.None;
            if (!string.IsNullOrEmpty(path))
                _probed = AssetDatabase.LoadMainAssetAtPath(path);
            return DragAndDropVisualMode.Link;
        }

        /// <summary>The object or asset under a screen point in the Hierarchy or Project window (null if none).</summary>
        public static void Probe(EditorWindow window, Vector2 screen, Action<UnityEngine.Object> done)
        {
            EditorApplication.delayCall += () =>
            {
                UnityEngine.Object found = null;
                if (window != null && !_probing)
                {
                    _probing = true;
                    _probed = null;
                    try
                    {
                        DragAndDrop.PrepareStartDrag();
                        DragAndDrop.SetGenericData(ProbeKey, true);
                        var at = ScreenToPanel(window, screen);
                        window.SendEvent(new Event { type = EventType.DragUpdated, mousePosition = at });
                        window.SendEvent(new Event { type = EventType.DragExited, mousePosition = at });
                    }
                    catch (Exception exception)
                    {
                        Debug.LogWarning("[Assistant] Couldn't see what's there: " + exception.Message);
                    }
                    finally
                    {
                        DragAndDrop.SetGenericData(ProbeKey, null);
                        _probing = false;
                    }
                    // A scene row is a Scene, not an object: nothing to say about it here.
                    found = _probed is GameObject || (_probed != null && EditorUtility.IsPersistent(_probed)) ? _probed : null;
                }
                done(found);
            };
        }

        #endregion

        #region Describing

        private sealed class Description
        {
            public string Title, Text;
        }

        private static InspectTarget Target(EditorWindow window, VisualElement element, Description description)
        {
            var bounds = element != null ? element.worldBound : window.rootVisualElement.worldBound;
            var local = window.rootVisualElement.WorldToLocal(bounds.position);
            return new InspectTarget
            {
                Title = description.Title,
                Text = description.Text,
                Window = window,
                ScreenRect = new Rect(window.position.position + local, bounds.size),
            };
        }

        // Hand-written words in the Sprocket Docs asset win; then the code's own [SchematicDoc].
        private static string Override(string key) => Docs.Find(key) is { } entry && !string.IsNullOrWhiteSpace(entry.Description) ? entry.Description : null;

        private static Description ForNode(Node node)
        {
            switch (node)
            {
                case SendSignalNode send when send.Signal != null:
                    return ForSignalNode(send.Signal, "Signal Node (Send)", "Sends");
                case OnSignalReceivedNode receive when receive.Signal != null:
                    return ForSignalNode(receive.Signal, "Signal Node (On Received)", "Runs when this arrives");
                case GetBehaviorVariableNode get when get.Target?.Type != null:
                    return ForVariableNode(get.Target.Type, get.Variable, "Variable Node (Get)");
                case SetBehaviorVariableNode set when set.Target?.Type != null:
                    return ForVariableNode(set.Target.Type, set.Variable, "Variable Node (Set)");
                case BehaviorReferenceNode behavior when behavior.Target?.Type != null:
                    return ForBehavior(behavior.Target.Type);
            }
            var type = node.GetType();
            var header = SchematicDocHeader.Of(type);
            return new Description
            {
                Title = header?.name ?? node.Name,
                Text = Override("node:" + type.FullName) ?? header?.Text ?? Docs.Find("node:" + type.FullName)?.Text ?? $"A {Nicify(type.Name)} node.",
            };
        }

        // "Signal Node (Send): " and then what the Signal itself is.
        private static Description ForSignalNode(SignalData signal, string label, string verb)
        {
            var (behavior, name) = SignalOwner(signal);
            var field = behavior?.GetField(name ?? "", BindingFlags.Instance | BindingFlags.Public);
            var header = SchematicDocHeader.Of(field);
            string about = header?.description ?? (behavior != null ? $"a Signal of {Nicify(behavior.Name)}." : $"the \"{signal.name}\" Signal.");
            return new Description
            {
                Title = $"{Nicify(name ?? signal.name)}" + (behavior != null ? $" ({Nicify(behavior.Name)})" : ""),
                Text = $"<b>{label}.</b> {verb} {Nicify(name ?? signal.name)}: {about}",
            };
        }

        private static Description ForVariableNode(Type behavior, string variable, string label)
        {
            var header = SchematicDocHeader.Of(behavior.GetField(variable ?? "", BindingFlags.Instance | BindingFlags.Public));
            string about = header?.description ?? $"a Variable of {Nicify(behavior.Name)}.";
            return new Description
            {
                Title = $"{Nicify(variable)} ({Nicify(behavior.Name)})",
                Text = $"<b>{label}.</b> {Nicify(variable)}{(header?.type != null ? $" ({header.type})" : "")}: {about}",
            };
        }

        private static Description ForBehavior(Type type)
        {
            var header = SchematicDocHeader.Of(type);
            return new Description
            {
                Title = Nicify(type.Name),
                Text = Override("behavior:" + type.FullName) ?? header?.Text ?? Docs.Find("behavior:" + type.FullName)?.Text ?? $"The {Nicify(type.Name)} Behavior.",
            };
        }

        private static Description ForDockItem(DockItem item)
        {
            switch (item.Kind)
            {
                case DockItem.ItemKind.Behavior when item.Behavior != null:
                    return ForBehavior(item.Behavior);
                case DockItem.ItemKind.Variable when item.Behavior != null:
                case DockItem.ItemKind.Signal when item.Behavior != null:
                {
                    bool signal = item.Kind == DockItem.ItemKind.Signal;
                    var field = item.Behavior.GetField(item.Name ?? "", BindingFlags.Instance | BindingFlags.Public);
                    var header = SchematicDocHeader.Of(field);
                    string key = $"{(signal ? "signal" : "variable")}:{item.Behavior.FullName}.{item.Name}";
                    string text = Override(key) ?? header?.description ?? Docs.Find(key)?.Text
                        ?? (signal ? $"A Signal of {Nicify(item.Behavior.Name)}." : $"A Variable of {Nicify(item.Behavior.Name)}: a value it keeps for each object.");
                    string how = signal
                        ? (header?.direction == "input" ? "Drag it into the graph to send it." : "Drag it into the graph to react to it.")
                        : "Drag it into the graph to Get or Set it.";
                    return new Description { Title = $"{Nicify(item.Name)} ({Nicify(item.Behavior.Name)})", Text = $"<b>{(signal ? "Signal" : "Variable")}.</b> {text} {how}" };
                }
                case DockItem.ItemKind.GameObject:
                    return new Description
                    {
                        Title = item.GameObject != null ? item.GameObject.name : "Prefab Object",
                        Text = "One of this prefab's objects. Behaviors attach to it; drag it into the graph to refer to it.",
                    };
            }
            return new Description { Title = item.DisplayName, Text = "Part of this prefab." };
        }

        /// <summary>The Behavior that declares a Signal asset, and the Signal's name there.</summary>
        private static (Type behavior, string name) SignalOwner(SignalData signal)
        {
            if (signal != null && AssetDatabase.LoadMainAssetAtPath(AssetDatabase.GetAssetPath(signal)) is BehaviorMetadata metadata)
                foreach (var definition in metadata.Signals)
                    if (definition.Asset == signal)
                        return (metadata.BehaviorType.Type, definition.Name);
            return (null, signal != null ? signal.name : null);
        }

        private static InspectTarget ForGameObject(GameObject gameObject)
        {
            var text = new StringBuilder();
            var schematic = gameObject.GetComponentInParent<SchematicObject>();
            if (schematic != null)
            {
                text.Append(schematic.gameObject == gameObject ? "A Schematic Object" : $"Part of the Schematic Object \"{schematic.name}\"");
                if (schematic.SchematicGraph != null) text.Append($", run by the graph \"{schematic.SchematicGraph.name}\"");
                text.Append('.');
                if (schematic.Behaviors.TryGetValue(gameObject, out var list) && list != null)
                    foreach (var attachment in list.Items)
                    {
                        var type = attachment?.Metadata != null ? attachment.Metadata.BehaviorType.Type : null;
                        if (type == null) continue;
                        text.Append($"\n• <b>{Nicify(type.Name)}</b>: {FirstSentence(ForBehavior(type).Text) ?? "a Behavior."}");
                    }
            }

            // Its Unity components, described.
            var components = gameObject.GetComponents<Component>().Where(c => c != null && !(c is Transform) && !(c is SchematicObject)).ToList();
            if (schematic == null)
                text.Append(components.Count == 0 ? "An empty object: just a position in the world, maybe a parent for others." : "An object with:");
            else if (components.Count > 0)
                text.Append("\nIts components:");
            foreach (var component in components.Take(6))
            {
                var type = component.GetType();
                var header = SchematicDocHeader.Of(type);
                string about = Override("component:" + type.FullName) ?? header?.description ?? UnityObjectDocs.For(type, UnityObjectDocs.Components);
                text.Append($"\n• <b>{Nicify(type.Name)}</b>{(about != null ? ": " + FirstSentence(about) : "")}");
            }
            if (components.Count > 6) text.Append($"\n…and {components.Count - 6} more.");
            if (PrefabUtility.IsPartOfPrefabInstance(gameObject))
            {
                var source = PrefabUtility.GetCorrespondingObjectFromSource(gameObject);
                if (source != null) text.Append($"\nIt comes from the prefab \"{source.transform.root.name}\".");
            }
            return new InspectTarget { Title = gameObject.name, Text = text.ToString() };
        }

        private static InspectTarget ForComponent(Component component)
        {
            var type = component.GetType();
            string about = Override("component:" + type.FullName) ?? SchematicDocHeader.Of(type)?.Text ?? UnityObjectDocs.For(type, UnityObjectDocs.Components)
                ?? (component is MonoBehaviour ? "A script on this object." : $"A {Nicify(type.Name)} component.");
            return new InspectTarget { Title = $"{Nicify(type.Name)} (on {component.gameObject.name})", Text = about };
        }

        /// <summary>The windows themselves, for when Sprocket is dropped on nothing in particular.</summary>
        public static readonly Dictionary<string, (string title, string text)> Windows = new(StringComparer.Ordinal)
        {
            ["SchematicGraphEditorWindow"] = ("The Schematic Graph", "Where an object's logic lives. Events (On Start, On Signal Received…) start flows that run through action nodes along the arrows. The IO Dock on the left lists the prefab's objects with their Behaviors, Variables and Signals; drag any of them in to use them. Drop me on a node or a row and I'll tell you about it."),
            ["SceneView"] = ("The Scene View", "Your level, where you place and arrange objects. Drop me on an object and I'll tell you what it is and which Behaviors it has."),
            ["GameView"] = ("The Game View", "What the player sees through the game's camera. Press Play to run the game here."),
            ["InspectorWindow"] = ("The Inspector", "The settings of whatever is selected, one block per component. Drop me on a block (or a setting in it) and I'll tell you what it does."),
            ["SceneHierarchyWindow"] = ("The Hierarchy", "Every object in the open scenes, as a tree: children sit under their parents and move with them. Drop me on one to hear about it."),
            ["HierarchyWindow"] = ("The Hierarchy", "Every object in the open scenes, as a tree: children sit under their parents and move with them. Drop me on one to hear about it."),
            ["ProjectBrowser"] = ("The Project Window", "All of the project's files: prefabs, graphs, scripts, art, sounds. Drop me on one to hear what it is."),
        };

        private static InspectTarget ForWindow(EditorWindow window)
        {
            if (window == null)
                return null;
            string name = window.GetType().Name;
            if (!Windows.TryGetValue(name, out var about))
                about = (window.titleContent?.text ?? Nicify(name), "One of the editor's windows.");
            string text = Override("window:" + name) ?? about.text;
            if (window is SceneView && PrefabStageUtility.GetCurrentPrefabStage() is { } stage)
                text = $"Prefab Mode: you're editing the prefab \"{stage.prefabContentsRoot.name}\" on its own. Changes save to the prefab, so every copy of it gets them. Drop me on a part of it to hear about it.";
            else if (window is SchematicGraphEditorWindow graph && graph.SchematicGraph != null)
                text = $"This is the graph \"{graph.SchematicGraph.name}\". " + text;
            else if (name == "GameView" && EditorApplication.isPlaying)
                text = "The game, running. " + text.Replace(" Press Play to run the game here.", "");
            return new InspectTarget { Title = about.title, Text = text, Window = window, ScreenRect = window.position };
        }

        private static InspectTarget ForAsset(UnityEngine.Object asset)
        {
            string text = null;
            if (asset is MonoScript script && script.GetClass() is Type scripted)
            {
                var header = SchematicDocHeader.Of(scripted);
                text = header != null ? $"A script: the {header.name} {header.kind}. {header.Text}" : null;
            }
            else if (asset is GameObject prefab && prefab.GetComponent<SchematicObject>() is SchematicObject schematic)
                text = "A Schematic prefab: an object with Schematic Behaviors and a Schematic Graph. " + ForGameObject(prefab).Text;
            else if (asset is GameObject model)
                text = UnityObjectDocs.Assets["GameObject"] + (model.GetComponentsInChildren<Component>().Length > 1 ? " " + ForGameObject(model).Text : "");
            text ??= Override("asset:" + asset.GetType().FullName) ?? UnityObjectDocs.For(asset.GetType(), UnityObjectDocs.Assets) ?? $"A {Nicify(asset.GetType().Name)} asset.";
            return new InspectTarget { Title = asset.name, Text = text };
        }

        #endregion

        #region Documentation

        private static SprocketDocs _docs;

        /// <summary>The documentation (generated the first time it's needed).</summary>
        public static SprocketDocs Docs
        {
            get
            {
                if (_docs != null) return _docs;
                _docs = Resources.Load<SprocketDocs>("SprocketDocs");
                if (_docs == null)
                    _docs = Generate();
                return _docs;
            }
        }

        [MenuItem("Tools/Remedy/Schematics/Assistant/Regenerate Documentation", priority = 30)]
        private static void RegenerateMenu()
        {
            var docs = Generate();
            Selection.activeObject = docs;
            Debug.Log($"[Assistant] Documentation regenerated: {docs.Entries.Count} entries. Write a Description on any entry to use your own words.");
        }

        /// <summary>
        /// Builds (or refreshes) the documentation asset from the code: summaries from the toolkit's source comments,
        /// node help, and Tooltips. Hand-written Descriptions are kept.
        /// </summary>
        public static SprocketDocs Generate()
        {
            string path = AssetPath();
            var docs = AssetDatabase.LoadAssetAtPath<SprocketDocs>(path);
            bool created = docs == null;
            if (created)
                docs = ScriptableObject.CreateInstance<SprocketDocs>();

            var keep = docs.Entries.Where(e => e != null && !string.IsNullOrEmpty(e.Key)).ToDictionary(e => e.Key, e => e.Description);
            var summaries = SourceSummaries();
            var entries = new Dictionary<string, SprocketDocs.Entry>();

            void Add(string key, string title, string summary)
            {
                entries[key] = new SprocketDocs.Entry { Key = key, Title = title, Summary = summary ?? "", Description = keep.TryGetValue(key, out var d) ? d : "" };
            }

            foreach (var type in NodeReflection.GetNodeTypes().Values)
            {
                if (type?.Type == null) continue;
                summaries.TryGetValue(type.Type.Name, out string summary);
                var text = new StringBuilder(SchematicDocHeader.Of(type.Type)?.description ?? summary ?? type.Help ?? "");
                var inputs = type.Ports.Where(p => p.Direction == PortDirection.Input && p.Type != typeof(ActionPort)).Select(p => p.Name).ToList();
                var outputs = type.Ports.Where(p => p.Direction == PortDirection.Output && p.Type != typeof(ActionPort)).Select(p => p.Name).ToList();
                if (inputs.Count > 0) text.Append($"\nIn: {string.Join(", ", inputs)}.");
                if (outputs.Count > 0) text.Append($"\nOut: {string.Join(", ", outputs)}.");
                Add("node:" + type.Type.FullName, string.IsNullOrEmpty(type.Name) ? Nicify(type.Type.Name) : type.Name, text.ToString().Trim());
            }

            foreach (var type in TypeCache.GetTypesDerivedFrom<SchematicBehavior>())
            {
                if (type.IsAbstract) continue;
                summaries.TryGetValue(type.Name, out string summary);
                Add("behavior:" + type.FullName, Nicify(type.Name), SchematicDocHeader.Of(type)?.description ?? summary ?? $"The {Nicify(type.Name)} Behavior.");
                foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
                {
                    string fieldType = field.FieldType.Name;
                    bool signal = fieldType.StartsWith("SignalHandle");
                    bool variable = fieldType.StartsWith("VariableHandle");
                    if (!signal && !variable) continue;
                    string tooltip = field.GetCustomAttribute<TooltipAttribute>()?.tooltip;
                    summaries.TryGetValue(type.Name + "." + field.Name, out string fieldSummary);
                    Add($"{(signal ? "signal" : "variable")}:{type.FullName}.{field.Name}", $"{Nicify(field.Name)} ({Nicify(type.Name)})",
                        SchematicDocHeader.Of(field)?.description ?? tooltip ?? fieldSummary ?? (signal ? $"A Signal of {Nicify(type.Name)}." : $"A Variable of {Nicify(type.Name)}."));
                }
            }

            foreach (var pair in Windows)
                Add("window:" + pair.Key, pair.Value.title, pair.Value.text);
            Add("window:GameObject", "Prefab Object", "One of this prefab's objects. Behaviors attach to it; drag it into the graph to refer to it.");

            docs.Entries = entries.Values.OrderBy(e => e.Key, StringComparer.Ordinal).ToList();
            docs.Invalidate();
            if (created)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                AssetDatabase.CreateAsset(docs, path);
            }
            EditorUtility.SetDirty(docs);
            AssetDatabase.SaveAssetIfDirty(docs);
            _docs = docs;
            return docs;
        }

        // Next to this script: .../Assistant/Resources/SprocketDocs.asset
        private static string AssetPath()
        {
            foreach (var guid in AssetDatabase.FindAssets("SprocketInspect t:MonoScript"))
            {
                string script = AssetDatabase.GUIDToAssetPath(guid);
                if (script.EndsWith("/SprocketInspect.cs"))
                    return Path.GetDirectoryName(script).Replace('\\', '/') + "/Resources/SprocketDocs.asset";
            }
            return "Assets/Sprocket/Resources/SprocketDocs.asset";
        }

        private static readonly Regex _summaryBefore = new(
            @"///\s*<summary>(?<text>(?:(?!</summary>).)*)</summary>(?<between>(?:\s*(?:///[^\n]*|\[[^\n]*\]))*)\s*(?:public|internal|protected|private)?\s*(?:static\s+|sealed\s+|abstract\s+|readonly\s+|partial\s+)*(?:(?<kind>class|struct)\s+(?<type>\w+)|[\w<>,\s\[\]]+?\s(?<field>\w+)\s*[=;])",
            RegexOptions.Singleline | RegexOptions.Compiled);

        /// <summary>Summary comments from the toolkit's own source: by class name, and "Class.Field" for fields.</summary>
        private static Dictionary<string, string> SourceSummaries()
        {
            var summaries = new Dictionary<string, string>(StringComparer.Ordinal);
            string root = ToolkitRoot();
            if (root == null) return summaries;
            foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                if (file.Contains("~") || file.Contains("/Dependencies/") || file.Contains("\\Dependencies\\")) continue;
                string source;
                try { source = File.ReadAllText(file); }
                catch { continue; }
                string currentClass = null;
                foreach (Match match in _summaryBefore.Matches(source))
                {
                    string text = Clean(match.Groups["text"].Value);
                    if (match.Groups["type"].Success)
                    {
                        currentClass = match.Groups["type"].Value;
                        summaries.TryAdd(currentClass, text);
                    }
                    else if (match.Groups["field"].Success && currentClass != null)
                        summaries.TryAdd(currentClass + "." + match.Groups["field"].Value, text);
                }
            }
            return summaries;
        }

        private static string ToolkitRoot()
        {
            foreach (var guid in AssetDatabase.FindAssets("SprocketInspect t:MonoScript"))
            {
                string script = Path.GetFullPath(AssetDatabase.GUIDToAssetPath(guid));
                for (var dir = new DirectoryInfo(Path.GetDirectoryName(script)); dir != null; dir = dir.Parent)
                    if (Directory.Exists(Path.Combine(dir.FullName, "Schematics")) && Directory.Exists(Path.Combine(dir.FullName, "Features")))
                        return dir.FullName;
            }
            return null;
        }

        // XML doc comments into plain text: <see cref="A.B"/> → B, <c>x</c> → x, paragraphs kept.
        private static string Clean(string xml)
        {
            string text = Regex.Replace(xml, @"^\s*///\s?", "", RegexOptions.Multiline);
            text = Regex.Replace(text, @"<see\s+cref=""(?:[\w.]*\.)?([\w`]+)[^""]*""\s*/>", m => Nicify(m.Groups[1].Value));
            text = Regex.Replace(text, @"<paramref\s+name=""(\w+)""\s*/>", "$1");
            text = Regex.Replace(text, @"</?para>", "\n");
            text = Regex.Replace(text, @"<code>.*?</code>", "", RegexOptions.Singleline);
            text = Regex.Replace(text, @"<[^>]+>", "");
            text = text.Replace("&lt;", "<").Replace("&gt;", ">").Replace("&amp;", "&");
            text = Regex.Replace(text, @"[ \t]*\n[ \t]*", "\n");
            text = Regex.Replace(text, @"(?<!\n)\n(?!\n)", " ");
            return Regex.Replace(text, @"[ \t]{2,}", " ").Trim();
        }

        public static string Nicify(string name) => string.IsNullOrEmpty(name) ? "" : ObjectNames.NicifyVariableName(name);

        private static string FirstSentence(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            int end = text.IndexOfAny(new[] { '.', '\n' });
            return end > 0 && end < 160 ? text.Substring(0, end + 1).Trim() : text.Length > 160 ? text.Substring(0, 157) + "…" : text;
        }

        #endregion
    }
}
