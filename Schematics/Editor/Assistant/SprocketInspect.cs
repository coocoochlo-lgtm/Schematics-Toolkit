using BlueGraph;
using BlueGraph.Editor;
using System;
using System.Collections.Generic;
using System.Globalization;
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
        /// <summary>True when ScreenRect is the thing's real outline (false: just the spot it was found at).</summary>
        public bool Exact;
        /// <summary>Where it is now, for following it when the view moves (null: it stays put).</summary>
        public Func<Rect?> Track;
        public EditorWindow Window;
    }

    /// <summary>
    /// "What is this?": finds what's under a screen point (a graph node, an IO Dock row, an object in the Scene view,
    /// an object or piece of UI in the Game view, or for windows without a way to point at items, what's selected) and
    /// looks it up in the documentation. In Play Mode it also says what the object's Behaviors hold right now.
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
                    var picked = SelectionBaseInScene(sceneView, PickInScene(sceneView, screen));
                    var target = picked != null ? Placed(ForGameObject(picked), window, screen, ScreenBoundsInScene(sceneView, picked)) : null;
                    if (target != null && target.Exact)
                        target.Track = () => picked != null ? ScreenBoundsInScene(sceneView, picked) : null;
                    Finish(target);
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
                    var picked = InNewHierarchy(window, screen, out var row);
                    Finish(picked != null ? Placed(ForGameObject(picked), window, screen, ElementScreenRect(window, row)) : null);
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
                case "GameView":
                    Finish(InGame(window, screen));
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
                    done(SelectionBaseInScene(sceneView, PickInScene(sceneView, screen))?.name);
                    return;
                case SchematicGraphEditorWindow graph:
                    done(InGraph(graph, screen)?.Title);
                    return;
            }
            switch (window.GetType().Name)
            {
                case "HierarchyWindow":
                    done(InNewHierarchy(window, screen, out _)?.name);
                    return;
                case "SceneHierarchyWindow":
                case "ProjectBrowser":
                    Probe(window, screen, picked => done(picked?.name));
                    return;
                case "InspectorWindow":
                    done(InInspector(window, screen)?.Title);
                    return;
                case "GameView":
                    done(GameAt(window, screen)?.Name);
                    return;
            }
            done(null);
        }

        private static InspectTarget Placed(InspectTarget target, EditorWindow window, Vector2 screen, Rect? outline = null)
        {
            target.Window = window;
            target.Exact = outline.HasValue;
            target.ScreenRect = outline ?? new Rect(screen - new Vector2(30, 12), new Vector2(60, 24));
            return target;
        }

        /// <summary>An element's rectangle on the screen.</summary>
        private static Rect? ElementScreenRect(EditorWindow window, VisualElement element)
        {
            if (element == null || element.panel == null) return null;
            var bounds = element.worldBound;
            return new Rect(PanelToScreen(window, bounds.position), bounds.size);
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

        /// <summary>A screen point in a window's root space (where Sprocket is placed).</summary>
        public static Vector2 ScreenToRoot(EditorWindow window, Vector2 screen)
        {
            var root = window.rootVisualElement;
            return root != null && root.panel != null ? root.WorldToLocal(ScreenToPanel(window, screen)) : screen - window.position.position;
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

        /// <summary>
        /// The object at a screen point in a Scene view (null if none). Forgiving: if nothing is right under the
        /// point, it looks in widening rings around it and takes the closest thing.
        /// </summary>
        public static GameObject PickInScene(SceneView view, Vector2 screen)
        {
            var camera = view != null ? view.camera : null;
            if (camera == null)
                return null;

            // The point inside the camera's rectangle.
            var screenViewport = SceneViewport(view);
            var viewport = new Rect(Vector2.zero, screenViewport.size);
            var local = screen - screenViewport.position;
            if (viewport.width < 1f || viewport.height < 1f || !viewport.Contains(local))
                return null;
            var pixels = camera.pixelRect;
            float pixelsPerPoint = pixels.width / viewport.width;
            Vector2 PixelAt(Vector2 point) => new(pixels.x + point.x * pixelsPerPoint, pixels.y + (viewport.height - point.y) * pixelsPerPoint);
            var pixel = PixelAt(local);

            var stage = StageUtility.GetCurrentStageHandle();
            var visibility = SceneVisibilityManager.instance;
            bool Pickable(GameObject gameObject) => gameObject.activeInHierarchy && !visibility.IsHidden(gameObject) && !visibility.IsPickingDisabled(gameObject);

            // Icons first: they're drawn over everything (and are generous targets).
            GameObject icon = null;
            float iconDistance = float.MaxValue;
            if (view.drawGizmos)
                foreach (var component in IconComponents(stage))
                {
                    if (component == null || component.TryGetComponent<Renderer>(out _) || !Pickable(component.gameObject))
                        continue;
                    var at = camera.WorldToScreenPoint(component.transform.position);
                    if (at.z <= camera.nearClipPlane)
                        continue;
                    float distance = Vector2.Distance(new Vector2(at.x, at.y), pixel) / pixelsPerPoint;
                    if (distance <= IconRadius + 6f && distance < iconDistance)
                    {
                        icon = component.gameObject;
                        iconDistance = distance;
                    }
                }
            if (icon != null)
                return icon;

            var renderers = new List<Renderer>();
            foreach (var renderer in stage.FindComponentsOfType<Renderer>())
                if (renderer != null && renderer.enabled && !(renderer is ParticleSystemRenderer) && Pickable(renderer.gameObject))
                    renderers.Add(renderer);
            var physics = PhysicsSceneOf(stage);

            // Right under the point, then rings around it (8, 16, 24 points out).
            bool box3D = EditorApplication.isPlaying;
            return InRings(local, viewport, point => RayPick(camera.ScreenPointToRay(PixelAt(point)), renderers, physics, Physics.DefaultRaycastLayers, Pickable, box3D, out _));
        }

        /// <summary>
        /// Tries a pick right at a point, then on rings around it (8, 16, 24 points out) until something is found:
        /// forgiving, so small or thin things can be found without pixel-perfect aim.
        /// </summary>
        private static GameObject InRings(Vector2 local, Rect area, Func<Vector2, GameObject> pick)
        {
            var picked = pick(local);
            for (int ring = 1; picked == null && ring <= 3; ring++)
            {
                float radius = ring * 8f;
                int samples = 8 * ring;
                for (int i = 0; i < samples && picked == null; i++)
                {
                    float angle = i * Mathf.PI * 2f / samples;
                    var point = local + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                    if (area.Contains(point))
                        picked = pick(point);
                }
            }
            return picked;
        }

        /// <summary>
        /// The nearest object a ray meets: renderers whose bounds it crosses (nearest first) checked against their
        /// meshes, then colliders without a renderer (terrain…) through Unity physics, and in Play Mode the toolkit's
        /// Box3D physics bodies.
        /// </summary>
        private static GameObject RayPick(Ray ray, List<Renderer> renderers, PhysicsScene physics, int mask, Func<GameObject, bool> pickable, bool box3D,
            out float distanceAlong)
        {
            var candidates = new List<(Renderer renderer, float distance)>();
            foreach (var renderer in renderers)
                if (renderer != null && renderer.bounds.IntersectRay(ray, out float distance))
                    candidates.Add((renderer, distance));
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
            if (physics.IsValid() && physics.Raycast(ray.origin, ray.direction, out var physicsHit, bestDistance, mask)
                && physicsHit.collider != null && pickable(physicsHit.collider.gameObject))
            {
                best = physicsHit.collider.gameObject;
                bestDistance = physicsHit.distance;
            }

            // Box3D bodies (the toolkit's physics), while the game runs.
            if (box3D && Box3DRaycast(ray, bestDistance, mask, out float box3DDistance) is { } body && box3DDistance < bestDistance && pickable(body))
            {
                best = body;
                bestDistance = box3DDistance;
            }
            distanceAlong = bestDistance;
            return best;
        }

        // Remedy.Box3DPhysics.SchematicPhysics.SphereCast, found by name: this assembly doesn't reference the physics
        // one, and only asks while the game runs and a Box3D world exists (asking otherwise would make one).
        private static MethodInfo _box3DSphereCast;
        private static Type _box3DWorldType;
        private static bool _box3DLooked;

        private static GameObject Box3DRaycast(Ray ray, float maxDistance, int mask, out float distance)
        {
            distance = float.MaxValue;
            if (!EditorApplication.isPlaying)
                return null;
            if (!_box3DLooked)
            {
                _box3DLooked = true;
                _box3DSphereCast = Type.GetType("Remedy.Box3DPhysics.SchematicPhysics, Remedy.Physics")?.GetMethod("SphereCast", BindingFlags.Public | BindingFlags.Static);
                _box3DWorldType = Type.GetType("Box3D.Hybrid.Box3DWorld, Box3D.Hybrid");
            }
            if (_box3DSphereCast == null || _box3DWorldType == null || UnityEngine.Object.FindAnyObjectByType(_box3DWorldType) == null)
                return null;
            try
            {
                float limit = Mathf.Min(maxDistance, 10000f);
                // (origin, radius, direction, out hit, out hitObject, distance, mask, ignore)
                var args = new object[] { ray.origin, 0f, ray.direction, null, null, limit, (LayerMask)mask, null };
                if (!(bool)_box3DSphereCast.Invoke(null, args) || !(args[4] is GameObject hitObject) || hitObject == null)
                    return null;
                distance = ((RaycastHit)args[3]).distance;
                return hitObject;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// What a click there would select, like Unity does: the object marked [SelectionBase] above it, or the prefab
        /// it's part of, as long as that isn't so big it fills the view.
        /// </summary>
        public static GameObject SelectionBaseInScene(SceneView view, GameObject picked)
            => SelectionBase(picked, root => ScreenBoundsInScene(view, root).HasValue);

        // The same, with "does it fit the view" asked of the caller (the Scene and Game views measure differently).
        private static GameObject SelectionBase(GameObject picked, Func<GameObject, bool> fits)
        {
            if (picked == null)
                return null;
            for (var t = picked.transform; t != null; t = t.parent)
                foreach (var component in t.GetComponents<MonoBehaviour>())
                    if (component != null && component.GetType().IsDefined(typeof(SelectionBaseAttribute), true))
                        return t.gameObject;
            var root = PrefabUtility.GetOutermostPrefabInstanceRoot(picked);
            if (root != null && root != picked && fits(root))
                return root;
            return picked;
        }

        // The Scene camera's rectangle on the screen.
        private static Rect SceneViewport(SceneView view)
        {
            if (_cameraElement?.GetValue(view) is VisualElement cameraView && cameraView.panel != null)
            {
                var bounds = cameraView.worldBound;
                return new Rect(PanelToScreen(view, bounds.position), bounds.size);
            }
            var viewport = view.cameraViewport;
            return new Rect(view.position.position + new Vector2(0, view.position.height - viewport.height), viewport.size);
        }

        /// <summary>
        /// The bounds Unity frames an object by (its renderers and its children's together), else its colliders'.
        /// Null for objects with neither (lights, empties): they're shown as icons.
        /// </summary>
        public static Bounds? WorldBounds(GameObject gameObject)
        {
            Bounds? bounds = null;
            foreach (var renderer in gameObject.GetComponentsInChildren<Renderer>())
            {
                if (renderer == null || !renderer.enabled || renderer is ParticleSystemRenderer) continue;
                var b = renderer.bounds;
                if (b.size == Vector3.zero) continue;
                if (bounds.HasValue) { var all = bounds.Value; all.Encapsulate(b); bounds = all; }
                else bounds = b;
            }
            if (bounds.HasValue)
                return bounds;
            foreach (var collider in gameObject.GetComponentsInChildren<Collider>())
            {
                if (collider == null || !collider.enabled) continue;
                var b = collider.bounds;
                if (bounds.HasValue) { var all = bounds.Value; all.Encapsulate(b); bounds = all; }
                else bounds = b;
            }
            return bounds;
        }

        /// <summary>
        /// An object's outline on the screen in a Scene view: the box around its (and its children's) bounds, or its
        /// icon. Null when it's partly behind the camera or fills most of the view (then there's no use framing it).
        /// </summary>
        public static Rect? ScreenBoundsInScene(SceneView view, GameObject gameObject)
        {
            var camera = view != null ? view.camera : null;
            if (camera == null || gameObject == null)
                return null;
            var viewport = SceneViewport(view);
            var pixels = camera.pixelRect;
            if (viewport.width < 1f || pixels.width < 1f)
                return null;

            Vector2? ToScreen(Vector3 world)
            {
                var at = camera.WorldToScreenPoint(world);
                if (at.z <= camera.nearClipPlane) return null;
                return viewport.position + new Vector2((at.x - pixels.x) / pixels.width * viewport.width, (1f - (at.y - pixels.y) / pixels.height) * viewport.height);
            }
            return ScreenBounds(gameObject, ToScreen, viewport);
        }

        /// <summary>
        /// An object's outline on the screen, given how world points land on it: the box around its bounds, or a
        /// circle around its position when it has none (lights, empties). Null when it's partly behind the camera or
        /// fills most of the view.
        /// </summary>
        private static Rect? ScreenBounds(GameObject gameObject, Func<Vector3, Vector2?> toScreen, Rect viewport)
        {
            var bounds = WorldBounds(gameObject);
            if (!bounds.HasValue)
            {
                // Drawn as an icon.
                var icon = toScreen(gameObject.transform.position);
                return icon.HasValue ? new Rect(icon.Value - new Vector2(IconRadius, IconRadius), new Vector2(IconRadius * 2f, IconRadius * 2f)) : null;
            }

            var b = bounds.Value;
            Vector2 min = new(float.MaxValue, float.MaxValue), max = new(float.MinValue, float.MinValue);
            for (int i = 0; i < 8; i++)
            {
                var corner = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                var at = toScreen(corner);
                if (!at.HasValue) return null;
                min = Vector2.Min(min, at.Value);
                max = Vector2.Max(max, at.Value);
            }
            var rect = Rect.MinMaxRect(Mathf.Max(min.x, viewport.xMin), Mathf.Max(min.y, viewport.yMin), Mathf.Min(max.x, viewport.xMax), Mathf.Min(max.y, viewport.yMax));
            if (rect.width < 2f || rect.height < 2f || rect.width > viewport.width * 0.6f || rect.height > viewport.height * 0.6f)
                return null;
            return rect;
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
                MeshRenderer => renderer.TryGetComponent<MeshFilter>(out var filter) ? filter.sharedMesh : null,
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

        #region Game view

        // The Game view draws the game's picture inside its window: under its toolbar, centred, scaled to fit (or
        // zoomed with its Scale slider) and letterboxed when the game's aspect differs from the window's. A point is
        // mapped through that picture to the game's cameras and picked the same way as in the Scene view (meshes,
        // colliders, Box3D bodies), with the game's UI (UI Toolkit documents, uGUI canvases) on top. Works in Edit
        // Mode and Play Mode; in Play Mode the description includes live values.

        /// <summary>What's under a point in the Game view.</summary>
        private sealed class GameHit
        {
            public string Name;
            public GameObject GameObject;   // a world object, or a uGUI element (Canvas set)
            public Camera Camera;           // what a world object (or world-space canvas) was seen through
            public Canvas Canvas;           // a uGUI element's root canvas
            public VisualElement Element;   // a UI Toolkit element
            public UIDocument Document;
            public bool FullScreen;         // UI covering (nearly) the whole picture: a backdrop, so the world comes first
            public float Distance = float.MaxValue;
        }

        private static InspectTarget InGame(EditorWindow window, Vector2 screen)
        {
            var hit = GameAt(window, screen);
            if (hit == null)
                return null;
            if (hit.Element != null)
            {
                var element = hit.Element;
                var outline = hit.FullScreen ? null : PanelElementScreenRect(window, element);
                var target = Placed(ForUIElement(element, hit.Document), window, screen, outline);
                if (target.Exact) target.Track = () => element.panel != null ? PanelElementScreenRect(window, element) : null;
                return target;
            }
            if (hit.Canvas != null && hit.GameObject != null)
            {
                var gameObject = hit.GameObject;
                var camera = hit.Camera;
                var outline = hit.FullScreen ? null : CanvasElementScreenRect(window, gameObject.transform as RectTransform, camera);
                var description = ForGameObject(gameObject);
                description.Text = $"A piece of the game's UI (uGUI) on the canvas \"{hit.Canvas.name}\".{UIText(gameObject)}\n" + description.Text;
                var target = Placed(description, window, screen, outline);
                if (target.Exact) target.Track = () => gameObject != null ? CanvasElementScreenRect(window, gameObject.transform as RectTransform, camera) : null;
                return target;
            }
            if (hit.GameObject != null)
            {
                var picked = hit.GameObject;
                var camera = hit.Camera;
                var target = Placed(ForGameObject(picked), window, screen, ScreenBoundsInGame(window, camera, picked));
                if (target.Exact) target.Track = () => picked != null && camera != null ? ScreenBoundsInGame(window, camera, picked) : null;
                return target;
            }
            return null;
        }

        /// <summary>
        /// What's at a screen point in the Game view: the game's UI first (it's drawn over the world), then the world
        /// through the game's cameras (topmost camera first), then world-space canvases if they're nearer.
        /// </summary>
        private static GameHit GameAt(EditorWindow window, Vector2 screen)
        {
            if (window == null)
                return null;
            var game = GameViewRect(window);
            if (game.width < 1f || game.height < 1f || !game.Contains(screen))
                return null;   // the toolbar, or the letterbox bars: the window itself
            var cameras = GameCameras(window);

            var document = UIDocumentAt(game, screen);
            if (document != null && !document.FullScreen)
                return document;
            var canvas = CanvasAt(window, game, screen, cameras, worldSpace: false);
            if (canvas != null && !canvas.FullScreen)
                return canvas;

            var world = PickInGame(game, screen, cameras);
            var worldCanvas = CanvasAt(window, game, screen, cameras, worldSpace: true);
            if (worldCanvas != null && (world == null || worldCanvas.Distance < world.Distance))
                return worldCanvas;
            if (world != null)
            {
                var camera = world.Camera;
                world.GameObject = SelectionBase(world.GameObject, root => ScreenBoundsInGame(window, camera, root).HasValue);
                world.Name = world.GameObject.name;
                return world;
            }
            return document ?? canvas;
        }

        private const BindingFlags AnyMember = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        private static readonly Dictionary<(Type, string), MemberInfo> _members = new();

        // A property or field of an editor internal, read by name (null if this Unity version doesn't have it).
        private static object Read(object target, string name)
        {
            if (target == null)
                return null;
            var key = (target.GetType(), name);
            if (!_members.TryGetValue(key, out var member))
            {
                try
                {
                    for (var type = target.GetType(); type != null && member == null; type = type.BaseType)
                    {
                        var property = type.GetProperty(name, AnyMember);
                        member = property != null && property.GetIndexParameters().Length == 0 && property.GetMethod != null
                            ? property
                            : type.GetField(name, AnyMember);
                    }
                }
                catch (AmbiguousMatchException) { member = null; }
                _members[key] = member;
            }
            try
            {
                return member switch
                {
                    PropertyInfo property => property.GetValue(property.GetMethod.IsStatic ? null : target),
                    FieldInfo field => field.GetValue(field.IsStatic ? null : target),
                    _ => null,
                };
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Where the Game view shows the game's picture, on the screen. Unity's own numbers when it has them (its
        /// view area under the toolbar, and the picture's rectangle in it, zoom and scrolling included); otherwise
        /// worked out from the game's resolution, scaled to fit and centred under the toolbar.
        /// </summary>
        public static Rect GameViewRect(EditorWindow window)
        {
            var content = window.position;
            if (Read(window, "viewInWindow") is Rect view && Read(window, "targetInView") is Rect target
                && view.width >= 1f && view.height >= 1f && target.width >= 1f && target.height >= 1f)
            {
                var rect = new Rect(content.position + view.position + target.position, target.size);
                if (rect.Overlaps(content))
                    return rect;
            }

            float toolbar = ToolbarHeight();
            var area = new Rect(content.x, content.y + toolbar, content.width, Mathf.Max(1f, content.height - toolbar));
            Vector2 pixels = Read(window, "targetSize") is Vector2 size && size.x >= 1f && size.y >= 1f ? size : Handles.GetMainGameViewSize();
            if (pixels.x < 1f || pixels.y < 1f)
                return area;
            var points = pixels / Mathf.Max(0.01f, EditorGUIUtility.pixelsPerPoint);
            float fit = Mathf.Min(area.width / points.x, area.height / points.y);
            float scale = Read(Read(window, "m_ZoomArea"), "scale") is Vector2 zoom && zoom.x > 0f ? zoom.x : Mathf.Min(1f, fit);
            var shown = points * scale;
            return new Rect(area.center - shown * 0.5f, shown);
        }

        // The height of a window's toolbar (EditorGUI.kWindowToolbarHeight: a number, or a style value, by version).
        private static float ToolbarHeight()
        {
            try
            {
                var field = typeof(EditorGUI).GetField("kWindowToolbarHeight", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                var value = field?.GetValue(null);
                switch (value)
                {
                    case float f when f > 0f: return f;
                    case int i when i > 0: return i;
                    case null: break;
                    default:
                        if (Read(value, "value") is float styled && styled > 0f) return styled;
                        break;
                }
            }
            catch
            {
                // Unknown on this version: the usual height.
            }
            return 21f;
        }

        // A screen point as a fraction of the game's picture: x from the left, y from the bottom (like viewports).
        private static Vector2 GameFraction(Rect game, Vector2 screen)
            => new((screen.x - game.x) / game.width, 1f - (screen.y - game.y) / game.height);

        private static Vector2 GameScreenPoint(Rect game, Vector2 fraction)
            => new(game.x + fraction.x * game.width, game.y + (1f - fraction.y) * game.height);

        // A fraction of the picture in a camera's own viewport (its Viewport Rect can be a part of the screen).
        private static Vector3 ToViewport(Camera camera, Vector2 fraction)
        {
            var r = camera.rect;
            return new Vector3((fraction.x - r.x) / r.width, (fraction.y - r.y) / r.height, 0f);
        }

        /// <summary>The cameras drawing into the Game view's display, topmost (highest depth) first.</summary>
        private static List<Camera> GameCameras(EditorWindow window)
        {
            int display = Read(window, "targetDisplay") is int d ? d : 0;
            var cameras = Camera.allCameras
                .Where(c => c != null && c.targetTexture == null && c.cameraType == CameraType.Game && c.targetDisplay == display
                    && c.rect.width > 0.001f && c.rect.height > 0.001f)
                .OrderByDescending(c => c.depth)
                .ToList();
            var main = Camera.main;
            if (cameras.Count == 0 && main != null)
                cameras.Add(main);
            return cameras;
        }

        // The world object at a point, through each camera in turn (the first that sees something wins).
        private static GameHit PickInGame(Rect game, Vector2 screen, List<Camera> cameras)
        {
            var area = new Rect(Vector2.zero, game.size);
            var local = screen - game.position;
            if (!area.Contains(local) || cameras.Count == 0)
                return null;
            var all = new List<Renderer>();
            foreach (var renderer in StageUtility.GetMainStageHandle().FindComponentsOfType<Renderer>())
                if (renderer != null && renderer.enabled && renderer.gameObject.activeInHierarchy && !(renderer is ParticleSystemRenderer))
                    all.Add(renderer);
            bool box3D = EditorApplication.isPlaying;

            foreach (var camera in cameras)
            {
                var r = camera.rect;
                if (!r.Contains(GameFraction(game, screen)))
                    continue;
                int mask = camera.cullingMask;
                var renderers = all.Where(x => (mask & (1 << x.gameObject.layer)) != 0).ToList();
                bool Pickable(GameObject gameObject) => gameObject != null && gameObject.activeInHierarchy && (mask & (1 << gameObject.layer)) != 0;
                float distance = float.MaxValue;
                var seenBy = camera;
                var picked = InRings(local, area, point =>
                {
                    var fraction = GameFraction(game, game.position + point);
                    if (!r.Contains(fraction))
                        return null;
                    var found = RayPick(seenBy.ViewportPointToRay(ToViewport(seenBy, fraction)), renderers, Physics.defaultPhysicsScene, mask, Pickable, box3D, out float along);
                    if (found != null) distance = along;
                    return found;
                });
                if (picked != null)
                    return new GameHit { GameObject = picked, Camera = camera, Name = picked.name, Distance = distance };
            }
            return null;
        }

        /// <summary>
        /// An object's outline in the Game view (the box around its bounds, through the camera it was seen with), or
        /// null when it's partly behind the camera or fills most of the picture.
        /// </summary>
        private static Rect? ScreenBoundsInGame(EditorWindow window, Camera camera, GameObject gameObject)
        {
            if (window == null || camera == null || gameObject == null)
                return null;
            var game = GameViewRect(window);
            var r = camera.rect;
            var viewport = new Rect(GameScreenPoint(game, new Vector2(r.x, r.yMax)), new Vector2(r.width * game.width, r.height * game.height));
            if (viewport.width < 1f || viewport.height < 1f)
                return null;
            Vector2? ToScreen(Vector3 world)
            {
                var at = camera.WorldToViewportPoint(world);
                if (at.z <= camera.nearClipPlane) return null;
                return GameScreenPoint(game, new Vector2(r.x + at.x * r.width, r.y + at.y * r.height));
            }
            return ScreenBounds(gameObject, ToScreen, viewport);
        }

        #region UI Toolkit (runtime)

        // The topmost UI Toolkit element at the point, over every screen-space UI Document (highest sorting first).
        // A runtime panel's root covers the whole game picture, so the point maps across in proportion.
        private static GameHit UIDocumentAt(Rect game, Vector2 screen)
        {
            var fraction = GameFraction(game, screen);
            var documents = UnityEngine.Object.FindObjectsByType<UIDocument>(FindObjectsSortMode.None)
                .Where(d => d != null && d.isActiveAndEnabled && d.rootVisualElement?.panel != null && d.panelSettings != null
                    && d.panelSettings.targetTexture == null
                    && !(Read(d.panelSettings, "renderMode") is Enum mode && mode.ToString() == "WorldSpace"))
                .OrderByDescending(d => d.panelSettings.sortingOrder)
                .ThenByDescending(d => d.sortingOrder)
                .ToList();
            GameHit backdrop = null;
            var asked = new HashSet<IPanel>();
            foreach (var document in documents)
            {
                var panel = document.rootVisualElement.panel;
                if (!asked.Add(panel))
                    continue;
                var size = panel.visualTree.layout.size;
                if (float.IsNaN(size.x) || size.x < 1f || size.y < 1f)
                    continue;
                var picked = panel.Pick(new Vector2(fraction.x * size.x, (1f - fraction.y) * size.y));
                if (picked == null)
                    continue;
                // Documents with the same Panel Settings share a panel: the one the element belongs to.
                var owner = documents.FirstOrDefault(d => d.rootVisualElement != null && d.rootVisualElement.panel == panel
                    && (d.rootVisualElement == picked || d.rootVisualElement.Contains(picked)));
                if (owner == null)
                    continue;
                var element = Meaningful(picked, owner.rootVisualElement);
                if (element == null)
                    continue;
                var bounds = element.worldBound;
                var hit = new GameHit
                {
                    Element = element,
                    Document = owner,
                    Name = string.IsNullOrEmpty(element.name) ? Nicify(element.GetType().Name) : element.name,
                    FullScreen = bounds.width * bounds.height >= size.x * size.y * 0.85f,
                };
                if (!hit.FullScreen)
                    return hit;
                backdrop ??= hit;
            }
            return backdrop;
        }

        // The element worth talking about: the control it's part of (a Button's label is the Button), else itself;
        // nothing if it's just the document's own container.
        private static VisualElement Meaningful(VisualElement picked, VisualElement root)
        {
            for (var element = picked; element != null && element != root; element = element.parent)
                if (element is Button || element is Toggle || element is TextField || element is Slider || element is SliderInt
                    || element is DropdownField || element is ProgressBar || element is ListView || element is ScrollView)
                    return element;
            return picked == root ? null : picked;
        }

        private static Rect? PanelElementScreenRect(EditorWindow window, VisualElement element)
        {
            var tree = element?.panel?.visualTree;
            if (tree == null || window == null)
                return null;
            var size = tree.layout.size;
            if (float.IsNaN(size.x) || size.x < 1f || size.y < 1f)
                return null;
            var game = GameViewRect(window);
            var b = element.worldBound;
            return new Rect(game.x + b.x / size.x * game.width, game.y + b.y / size.y * game.height,
                b.width / size.x * game.width, b.height / size.y * game.height);
        }

        private static InspectTarget ForUIElement(VisualElement element, UIDocument document)
        {
            var type = element.GetType();
            string kind = Nicify(type.Name);
            var text = new StringBuilder($"A {kind} in the game's UI (UI Toolkit), in the UI Document \"{document.name}\"");
            if (document.visualTreeAsset != null)
                text.Append($" (its layout is \"{document.visualTreeAsset.name}\")");
            text.Append('.');
            if (element is TextElement label && !string.IsNullOrWhiteSpace(label.text))
                text.Append($" It says “{Trim(label.text, 80)}”.");
            else if (element.Q<TextElement>() is { } inner && !string.IsNullOrWhiteSpace(inner.text))
                text.Append($" It says “{Trim(inner.text, 80)}”.");
            switch (element)
            {
                case Button:
                    text.Append(" Clicking it runs whatever the game's code hooked up to it (its clicked event).");
                    break;
                case ScrollView:
                case ListView:
                    text.Append(" A scrolling list of items.");
                    break;
            }
            // Fields and toggles: their value right now.
            if (type.GetProperty("value", BindingFlags.Instance | BindingFlags.Public) is { } valueProperty && valueProperty.GetIndexParameters().Length == 0)
            {
                try { text.Append($" Its value right now: {Show(valueProperty.GetValue(element))}."); }
                catch { /* not readable: leave it out */ }
            }
            if (!element.enabledInHierarchy)
                text.Append(" It's disabled at the moment.");
            var classes = element.GetClasses().Take(4).ToList();
            if (classes.Count > 0)
                text.Append($"\n<b>Style classes:</b> {string.Join(" ", classes.Select(c => "." + c))}");
            string title = string.IsNullOrEmpty(element.name) ? kind : $"{element.name} ({kind})";
            return new InspectTarget { Title = title, Text = text.ToString() };
        }

        #endregion

        #region uGUI (Canvas)

        // UnityEngine.UI (the uGUI package) isn't referenced by this assembly, so its types are found by name.
        private static Type _graphicType, _selectableType;
        private static bool _uguiLooked;

        /// <summary>
        /// The topmost uGUI element at the point: screen-space canvases (Overlay, and Camera ones) when
        /// <paramref name="worldSpace"/> is false, else world-space canvases (with the distance along the ray, so the
        /// caller can tell whether the world is in front).
        /// </summary>
        private static GameHit CanvasAt(EditorWindow window, Rect game, Vector2 screen, List<Camera> cameras, bool worldSpace)
        {
            if (!_uguiLooked)
            {
                _uguiLooked = true;
                _graphicType = Type.GetType("UnityEngine.UI.Graphic, UnityEngine.UI");
                _selectableType = Type.GetType("UnityEngine.UI.Selectable, UnityEngine.UI");
            }
            if (_graphicType == null)
                return null;
            var fraction = GameFraction(game, screen);
            int display = Read(window, "targetDisplay") is int d ? d : 0;

            var canvases = UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None)
                .Where(c => c != null && c.isActiveAndEnabled && c.isRootCanvas)
                .Where(c => worldSpace ? c.renderMode == RenderMode.WorldSpace : c.renderMode != RenderMode.WorldSpace)
                .Where(c => c.renderMode != RenderMode.ScreenSpaceOverlay || c.targetDisplay == display)
                // Overlay canvases draw over camera ones; higher sorting orders over lower.
                .OrderBy(c => c.renderMode == RenderMode.ScreenSpaceOverlay ? 0 : 1)
                .ThenByDescending(c => c.sortingOrder)
                .ToList();

            GameHit best = null, backdrop = null;
            foreach (var canvas in canvases)
            {
                Camera camera = null;
                Ray ray;
                var root = canvas.transform as RectTransform;
                if (root == null)
                    continue;
                if (canvas.renderMode == RenderMode.ScreenSpaceOverlay || (canvas.renderMode == RenderMode.ScreenSpaceCamera && canvas.worldCamera == null))
                {
                    // An overlay canvas lives in screen pixels: its root is the size of the screen.
                    var pixels = new Vector2(root.rect.width * root.lossyScale.x, root.rect.height * root.lossyScale.y);
                    ray = new Ray(new Vector3(fraction.x * pixels.x, fraction.y * pixels.y, -10000f), Vector3.forward);
                }
                else
                {
                    camera = canvas.renderMode == RenderMode.ScreenSpaceCamera
                        ? canvas.worldCamera
                        : (canvas.worldCamera != null ? canvas.worldCamera : cameras.FirstOrDefault(c => c.rect.Contains(fraction) && (c.cullingMask & (1 << canvas.gameObject.layer)) != 0));
                    if (camera == null || !camera.rect.Contains(fraction))
                        continue;
                    ray = camera.ViewportPointToRay(ToViewport(camera, fraction));
                }

                var element = GraphicAt(canvas, ray, out float distance);
                if (element == null)
                    continue;
                var shown = element;
                // A button's image or label: the button.
                if (_selectableType != null && element.GetComponentInParent(_selectableType) is Component selectable && selectable.transform is RectTransform control
                    && control.IsChildOf(canvas.transform))
                    shown = control;
                var rect = shown.rect;
                var canvasRect = root.rect;
                var hit = new GameHit
                {
                    GameObject = shown.gameObject,
                    Name = shown.name,
                    Canvas = canvas,
                    Camera = camera,
                    Distance = distance,
                    FullScreen = !worldSpace && Mathf.Abs(rect.width * shown.lossyScale.x * rect.height * shown.lossyScale.y)
                        >= Mathf.Abs(canvasRect.width * root.lossyScale.x * canvasRect.height * root.lossyScale.y) * 0.85f,
                };
                if (worldSpace)
                {
                    if (best == null || hit.Distance < best.Distance) best = hit;
                    continue;
                }
                if (!hit.FullScreen)
                    return hit;
                backdrop ??= hit;
            }
            return best ?? backdrop;
        }

        // The last-drawn (topmost) graphic of a canvas that the ray meets: later in the hierarchy draws on top.
        private static RectTransform GraphicAt(Canvas canvas, Ray ray, out float distance)
        {
            distance = float.MaxValue;
            RectTransform found = null;
            foreach (var graphic in canvas.GetComponentsInChildren(_graphicType, false))
            {
                if (!(graphic is Behaviour behaviour) || !behaviour.isActiveAndEnabled || !(graphic.transform is RectTransform rect))
                    continue;
                var plane = new Plane(rect.forward, rect.position);
                if (!plane.Raycast(ray, out float along))
                    continue;
                var local = rect.InverseTransformPoint(ray.GetPoint(along));
                if (!rect.rect.Contains(new Vector2(local.x, local.y)))
                    continue;
                found = rect;
                distance = along;
            }
            return found;
        }

        private static Rect? CanvasElementScreenRect(EditorWindow window, RectTransform rect, Camera camera)
        {
            if (window == null || rect == null)
                return null;
            var canvas = rect.GetComponentInParent<Canvas>();
            canvas = canvas != null ? canvas.rootCanvas : null;
            if (canvas == null || !(canvas.transform is RectTransform root))
                return null;
            var game = GameViewRect(window);
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);

            bool overlay = canvas.renderMode == RenderMode.ScreenSpaceOverlay || (canvas.renderMode == RenderMode.ScreenSpaceCamera && canvas.worldCamera == null);
            if (!overlay && camera == null)
                camera = canvas.worldCamera;
            if (!overlay && camera == null)
                return null;
            var pixels = new Vector2(root.rect.width * root.lossyScale.x, root.rect.height * root.lossyScale.y);
            if (overlay && (pixels.x < 1f || pixels.y < 1f))
                return null;

            Vector2 min = new(float.MaxValue, float.MaxValue), max = new(float.MinValue, float.MinValue);
            foreach (var corner in corners)
            {
                Vector2 fraction;
                if (overlay)
                    fraction = new Vector2(corner.x / pixels.x, corner.y / pixels.y);
                else
                {
                    var at = camera.WorldToViewportPoint(corner);
                    if (at.z <= 0f) return null;
                    var r = camera.rect;
                    fraction = new Vector2(r.x + at.x * r.width, r.y + at.y * r.height);
                }
                var point = GameScreenPoint(game, fraction);
                min = Vector2.Min(min, point);
                max = Vector2.Max(max, point);
            }
            var result = Rect.MinMaxRect(Mathf.Max(min.x, game.xMin), Mathf.Max(min.y, game.yMin), Mathf.Min(max.x, game.xMax), Mathf.Min(max.y, game.yMax));
            return result.width >= 2f && result.height >= 2f ? result : null;
        }

        // What a uGUI element's text says (Text, TextMeshPro…: anything with a string "text"), for the description.
        private static string UIText(GameObject gameObject)
        {
            foreach (var component in gameObject.GetComponentsInChildren<Component>())
            {
                if (component == null) continue;
                var property = component.GetType().GetProperty("text", BindingFlags.Instance | BindingFlags.Public);
                if (property == null || property.PropertyType != typeof(string) || property.GetIndexParameters().Length != 0) continue;
                try
                {
                    if (property.GetValue(component) is string said && !string.IsNullOrWhiteSpace(said))
                        return $" It says “{Trim(said, 80)}”.";
                }
                catch
                {
                    // Not readable right now.
                }
            }
            return "";
        }

        #endregion

        #endregion

        #region Live values (Play Mode)

        // While the game runs: what its Behaviors' Variables hold right now (and its graph's Variables), so "what is
        // this?" also answers "what is it doing?". A State Machine's Current State shows up this way too.
        private static void AppendLive(StringBuilder text, GameObject gameObject, SchematicObject schematic)
        {
            if (!EditorApplication.isPlaying || schematic == null || !schematic.IsSpawned && schematic.BehaviorInstances.Count == 0)
                return;
            var lines = new List<string>();
            foreach (var instance in schematic.BehaviorInstances)
            {
                if (instance == null || instance.GameObject != gameObject || !instance.IsSpawned)
                    continue;
                var behavior = instance.Behavior;
                if (behavior == null)
                    continue;
                var values = new List<string>();
                foreach (var field in behavior.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public))
                {
                    if (!field.FieldType.IsGenericType || field.FieldType.GetGenericTypeDefinition() != typeof(VariableHandle<>))
                        continue;
                    if (!TryLiveValue(field, behavior, instance, out object value))
                        continue;
                    values.Add($"{Nicify(field.Name)} = {Show(value)}");
                    if (values.Count >= 6)
                        break;
                }
                lines.Add($"• <b>{Nicify(behavior.GetType().Name)}</b>{(instance.Enabled ? "" : " (off)")}{(values.Count > 0 ? ": " + string.Join(", ", values) : "")}");
            }
            if (gameObject == schematic.gameObject)
                foreach (var store in schematic.GraphStores)
                {
                    if (store?.Graph == null || store.Count == 0)
                        continue;
                    var values = new List<string>();
                    for (int i = 0; i < store.Count && i < store.Graph.Variables.Count && values.Count < 6; i++)
                        if (store.Graph.Variables[i] != null)
                            values.Add($"{store.Graph.Variables[i].Name} = {Show(store.Get(i))}");
                    if (values.Count > 0)
                        lines.Add($"• <b>Graph \"{store.Graph.name}\"</b>: {string.Join(", ", values)}");
                }
            if (lines.Count == 0)
                return;
            text.Append("\n<b>Right now:</b>");
            foreach (var line in lines.Take(8))
                text.Append('\n').Append(line);
        }

        // A Variable's value for one instance: handle[instance], through reflection (the handle's T varies).
        private static bool TryLiveValue(FieldInfo field, SchematicBehavior behavior, BehaviorInstance instance, out object value)
        {
            value = null;
            try
            {
                var handle = field.GetValue(behavior);
                if (handle == null || field.FieldType.GetProperty("IsDefined")?.GetValue(handle) is false)
                    return false;
                var indexer = field.FieldType.GetProperty("Item", new[] { typeof(BehaviorInstance) });
                if (indexer == null)
                    return false;
                value = indexer.GetValue(handle, new object[] { instance });
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>A value, short and readable, for the bubble.</summary>
        private static string Show(object value)
        {
            switch (value)
            {
                case null: return "none";
                case UnityEngine.Object unityObject: return unityObject != null ? $"\"{unityObject.name}\"" : "none";
                case string text: return $"\"{Trim(text, 40)}\"";
                case bool flag: return flag ? "yes" : "no";
                case float number: return number.ToString("0.##", CultureInfo.InvariantCulture);
                case double number: return number.ToString("0.##", CultureInfo.InvariantCulture);
                case Vector2 v: return string.Format(CultureInfo.InvariantCulture, "({0:0.##}, {1:0.##})", v.x, v.y);
                case Vector3 v: return string.Format(CultureInfo.InvariantCulture, "({0:0.##}, {1:0.##}, {2:0.##})", v.x, v.y, v.z);
                case Quaternion q:
                {
                    var euler = q.eulerAngles;
                    return string.Format(CultureInfo.InvariantCulture, "({0:0.#}°, {1:0.#}°, {2:0.#}°)", euler.x, euler.y, euler.z);
                }
                case Color c: return "#" + ColorUtility.ToHtmlStringRGBA(c);
                case Enum e: return Nicify(e.ToString());
                case System.Collections.ICollection collection: return $"{collection.Count} item{(collection.Count == 1 ? "" : "s")}";
                default:
                {
                    string shown = value.ToString();
                    return shown == value.GetType().ToString() ? Nicify(value.GetType().Name) : Trim(shown, 40);
                }
            }
        }

        private static string Trim(string text, int length)
        {
            text = (text ?? "").Replace('\n', ' ').Trim();
            return text.Length > length ? text.Substring(0, length - 1) + "…" : text;
        }

        #endregion

        #region Hierarchy, Project and Inspector

        // Unity 6's Hierarchy window is made of UI elements: each row is a HierarchyViewItem whose handler knows its
        // GameObject. Read by reflection, so older editors (without it) are fine.
        private static GameObject InNewHierarchy(EditorWindow window, Vector2 screen, out VisualElement row)
        {
            row = null;
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
                    row = element;
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
            return new InspectTarget
            {
                Title = description.Title,
                Text = description.Text,
                Window = window,
                ScreenRect = new Rect(PanelToScreen(window, bounds.position), bounds.size),
                Exact = element != null,
                Track = element != null ? () => ElementScreenRect(window, element) : null,
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
            AppendLive(text, gameObject, schematic);
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
            ["GameView"] = ("The Game View", "What the player sees through the game's camera. Press Play to run the game here. Drop me on an object or a piece of UI in it and I'll tell you what it is, and while the game runs, what its Behaviors are up to right now."),
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
            else if (asset is GameObject prefab && prefab.TryGetComponent<SchematicObject>(out _))
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
