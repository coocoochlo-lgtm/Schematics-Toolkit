using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Remedy.Schematics.Templates
{
    /// <summary>
    /// "Create New Schematic Object": a small browser that opens next to the mouse from the Hierarchy, Scene view or
    /// Project window right-click menus. Pick a template and you get an instance of it in the scene (where you
    /// right-clicked, or under the object you right-clicked in the Hierarchy) or, from the Project window, a new prefab
    /// of your own in the selected folder (with its own copy of the template's graph), ready to rename.
    /// </summary>
    /// <remarks>
    /// The old way (dragging a GameObject into the Project window) still works exactly as before.
    /// </remarks>
    public class CreateSchematicObjectWindow : EditorWindow
    {
        /// <summary>Where the picked template goes.</summary>
        public enum Destination { Scene, Project }

        private const string VariantPref = "Remedy.Schematics.Templates.CreateAsVariant";
        private static readonly Vector2 WindowSize = new(560f, 470f);

        [SerializeField] private Destination _destination;
        [SerializeField] private Transform _parent;
        [SerializeField] private bool _hasPoint;
        [SerializeField] private Vector3 _point;
        [SerializeField] private string _folder;

        private SchematicTemplateGrid _grid;

        #region Opening

        /// <summary>Opens the browser to place a template in the scene.</summary>
        /// <param name="parent">Parent for the new object (null: scene root, or the open prefab's root).</param>
        /// <param name="worldPoint">Where to put it (null: the Scene view's pivot, or aligned to the parent).</param>
        /// <param name="screenPosition">Where to open the window (null: next to the mouse, as best known).</param>
        public static CreateSchematicObjectWindow ShowForScene(Transform parent = null, Vector3? worldPoint = null, Vector2? screenPosition = null)
        {
            var window = Open(screenPosition);
            window._destination = Destination.Scene;
            window._parent = parent;
            window._hasPoint = worldPoint.HasValue;
            window._point = worldPoint ?? Vector3.zero;
            window.RebuildHeader();
            return window;
        }

        /// <summary>Opens the browser to create a new prefab from a template in a Project folder.</summary>
        public static CreateSchematicObjectWindow ShowForProject(string folder = null, Vector2? screenPosition = null)
        {
            var window = Open(screenPosition);
            window._destination = Destination.Project;
            window._folder = string.IsNullOrEmpty(folder) ? SchematicObjectFactory.SelectedProjectFolder() : folder;
            window.RebuildHeader();
            return window;
        }

        private static CreateSchematicObjectWindow Open(Vector2? screenPosition)
        {
            var window = CreateInstance<CreateSchematicObjectWindow>();
            window.titleContent = new GUIContent("Create New Schematic Object");
            window.minSize = new Vector2(320f, 260f);
            window.ShowUtility();

            var at = screenPosition ?? SchematicContextClicks.RecentScreenPoint ?? FocusedWindowCenter();
            window.position = Clamp(new Rect(at.x - 24f, at.y - 12f, WindowSize.x, WindowSize.y));
            window.Focus();
            return window;
        }

        private static Vector2 FocusedWindowCenter()
        {
            var focused = focusedWindow;
            var area = focused != null ? focused.position : EditorGUIUtility.GetMainWindowPosition();
            return area.center - WindowSize * 0.5f;
        }

        // Keeps the window inside Unity's main window.
        private static Rect Clamp(Rect rect)
        {
            var main = EditorGUIUtility.GetMainWindowPosition();
            if (main.width <= 0 || main.height <= 0) return rect;
            rect.x = Mathf.Clamp(rect.x, main.xMin, Mathf.Max(main.xMin, main.xMax - rect.width));
            rect.y = Mathf.Clamp(rect.y, main.yMin, Mathf.Max(main.yMin, main.yMax - rect.height));
            return rect;
        }

        #endregion

        #region Menus

        // Unity calls a GameObject-menu item once per selected object; only the first call opens the window.
        private static double _lastMenuTime;

        private static bool FirstCallThisClick()
        {
            double now = EditorApplication.timeSinceStartup;
            if (now - _lastMenuTime < 0.25) return false;
            _lastMenuTime = now;
            return true;
        }

        /// <summary>
        /// GameObject > Schematics > Create New Schematic Object… (also in the Hierarchy's right-click menu, and the
        /// Scene view's in Unity 6). Right-clicked on an object: the new one goes under it. Right-clicked in the Scene
        /// view: it goes where you clicked.
        /// </summary>
        [MenuItem("GameObject/Schematics/Create New Schematic Object…", false, 10)]
        private static void FromGameObjectMenu(MenuCommand command)
        {
            if (!FirstCallThisClick()) return;

            var parent = (command.context as GameObject)?.transform;
            Vector3? point = null;
            if (SchematicContextClicks.LastWasScene)
            {
                point = SchematicContextClicks.SceneWorldPoint;
                parent = null; // a Scene view click means "here", not "under the selection"
            }
            ShowForScene(parent, point);
        }

        /// <summary>The Scene view's right-click menu (Unity 6's GameObject tool context).</summary>
        [MenuItem("CONTEXT/GameObjectToolContext/Create New Schematic Object…", false, 10)]
        private static void FromSceneContextMenu(MenuCommand command)
        {
            if (!FirstCallThisClick()) return;
            ShowForScene(null, SchematicContextClicks.LastWasScene ? SchematicContextClicks.SceneWorldPoint : (Vector3?)null);
        }

        /// <summary>Assets > Create > Schematics > Schematic Object… (Project window right-click).</summary>
        [MenuItem("Assets/Create/Schematics/Schematic Object…", false, 0)]
        private static void FromProjectMenu()
        {
            ShowForProject(SchematicObjectFactory.SelectedProjectFolder());
        }

        /// <summary>Opens the browser on its own, to look at and manage templates.</summary>
        [MenuItem("Tools/Remedy/Schematics/Templates/Template Browser", false, 0)]
        private static void OpenBrowser() => ShowForScene();

        #endregion

        private void CreateGUI()
        {
            var root = rootVisualElement;
            root.style.flexDirection = FlexDirection.Column;
            root.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Escape) { Close(); evt.StopPropagation(); }
            }, TrickleDown.TrickleDown);

            var header = new VisualElement { name = "header" };
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            header.style.flexShrink = 0;
            header.style.paddingLeft = 6;
            header.style.paddingRight = 6;
            header.style.paddingTop = 4;
            header.style.paddingBottom = 4;
            root.Add(header);

            _grid = new SchematicTemplateGrid("Remedy.Schematics.CreateWindow");
            _grid.Picked += Create;
            root.Add(_grid);

            var footer = new VisualElement();
            footer.style.flexDirection = FlexDirection.Row;
            footer.style.justifyContent = Justify.FlexEnd;
            footer.style.flexShrink = 0;
            footer.style.paddingLeft = 6;
            footer.style.paddingRight = 6;
            footer.style.paddingTop = 4;
            footer.style.paddingBottom = 6;
            footer.Add(new Button(Close) { text = "Cancel" });
            var create = new Button(() => _grid.PickSelected()) { text = "Create" };
            create.style.minWidth = 80;
            footer.Add(create);
            _grid.SelectionChanged += t => create.SetEnabled(t != null);
            root.Add(footer);

            RebuildHeader();
            _grid.schedule.Execute(_grid.FocusSearch);
        }

        private void RebuildHeader()
        {
            var header = rootVisualElement.Q("header");
            if (header == null) return;
            header.Clear();

            string where = _destination == Destination.Project
                ? $"New prefab in <b>{_folder}</b>"
                : _parent != null ? $"In the scene, under <b>{_parent.name}</b>"
                : _hasPoint ? "In the scene, where you clicked"
                : "In the scene";
            var label = new Label(where);
            label.style.flexGrow = 1;
            label.style.opacity = 0.8f;
            header.Add(label);

            if (_destination == Destination.Project)
            {
                var variant = new Toggle("Prefab Variant")
                {
                    value = EditorPrefs.GetBool(VariantPref, false),
                    tooltip = "On: the new prefab is a Variant of the template, so changes to the template's objects carry over. Off: an independent copy. Either way it gets its own copy of the Schematic Graph.",
                };
                variant.RegisterValueChangedCallback(evt => EditorPrefs.SetBool(VariantPref, evt.newValue));
                variant.labelElement.style.minWidth = 0;
                variant.labelElement.style.marginRight = 4;
                header.Add(variant);
            }
        }

        private void Create(SchematicTemplate template)
        {
            if (template?.Prefab == null)
                return;

            if (_destination == Destination.Project)
            {
                var prefab = SchematicObjectFactory.CreatePrefabFromTemplate(template, _folder, EditorPrefs.GetBool(VariantPref, false));
                if (prefab != null)
                    Close();
                return;
            }

            var instance = SchematicObjectFactory.InstantiateInScene(template, _parent, _hasPoint ? _point : (Vector3?)null);
            if (instance != null)
                Close();
        }
    }

    /// <summary>
    /// Remembers where the last right-click happened (Scene view, Hierarchy or Project window), so the Create window
    /// opens next to it and, for the Scene view, places the object on the surface that was clicked. Also lets
    /// template tiles be dragged from the browser into the Scene view.
    /// </summary>
    [InitializeOnLoad]
    internal static class SchematicContextClicks
    {
        // How long a right-click counts as "the one that opened this menu".
        private const double RecentSeconds = 10.0;

        private enum Source { None, Scene, Hierarchy, Project }

        private static Source _source;
        private static double _time;
        private static Vector2 _screenPoint;

        /// <summary>The world point under the last Scene view right-click.</summary>
        public static Vector3 SceneWorldPoint { get; private set; }

        /// <summary>The last right-click was in the Scene view, recently.</summary>
        public static bool LastWasScene => _source == Source.Scene && IsRecent;

        /// <summary>The screen position of the last right-click, if recent.</summary>
        public static Vector2? RecentScreenPoint => _source != Source.None && IsRecent ? _screenPoint : (Vector2?)null;

        private static bool IsRecent => EditorApplication.timeSinceStartup - _time < RecentSeconds;

        static SchematicContextClicks()
        {
            SceneView.duringSceneGui += OnSceneGUI;
#if UNITY_6000_2_OR_NEWER
            EditorApplication.hierarchyWindowItemByEntityIdOnGUI += (_, rect) => OnListGUI(Source.Hierarchy, rect);
#else
            EditorApplication.hierarchyWindowItemOnGUI += (_, rect) => OnListGUI(Source.Hierarchy, rect);
#endif
            EditorApplication.projectWindowItemOnGUI += (_, rect) => OnListGUI(Source.Project, rect);
        }

        private static bool IsRightClick(Event evt)
            => evt != null && (evt.type == EventType.ContextClick || (evt.type == EventType.MouseDown && evt.button == 1));

        private static void OnListGUI(Source source, Rect rect)
        {
            var evt = Event.current;
            if (!IsRightClick(evt) || !rect.Contains(evt.mousePosition))
                return;
            Record(source, GUIUtility.GUIToScreenPoint(evt.mousePosition));
        }

        private static void OnSceneGUI(SceneView view)
        {
            var evt = Event.current;
            if (evt == null) return;

            if (IsRightClick(evt))
            {
                Record(Source.Scene, GUIUtility.GUIToScreenPoint(evt.mousePosition));
                SceneWorldPoint = WorldPointUnder(evt.mousePosition, view);
                return;
            }

            HandleTemplateDrag(evt, view);
        }

        private static void Record(Source source, Vector2 screenPoint)
        {
            _source = source;
            _time = EditorApplication.timeSinceStartup;
            _screenPoint = screenPoint;
        }

        /// <summary>
        /// The surface under a Scene view point: what's drawn there (meshes, no colliders needed), else the ground
        /// plane (y = 0), else a spot in front of the camera.
        /// </summary>
        public static Vector3 WorldPointUnder(Vector2 guiPoint, SceneView view)
        {
            if (HandleUtility.PlaceObject(guiPoint, out var position, out _))
                return position;
            var ray = HandleUtility.GUIPointToWorldRay(guiPoint);
            var ground = new Plane(Vector3.up, Vector3.zero);
            if (ground.Raycast(ray, out float distance) && distance < 1000f)
                return ray.GetPoint(distance);
            return ray.GetPoint(view != null ? Mathf.Max(2f, view.cameraDistance) : 10f);
        }

        // Template tiles dragged from a browser: placed where they're dropped.
        private static void HandleTemplateDrag(Event evt, SceneView view)
        {
            if (evt.type != EventType.DragUpdated && evt.type != EventType.DragPerform)
                return;
            if (DragAndDrop.GetGenericData(SchematicTemplateGrid.DragDataKey) is not SchematicTemplate template || template.Prefab == null)
                return;

            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            if (evt.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                SchematicObjectFactory.InstantiateInScene(template, null, WorldPointUnder(evt.mousePosition, view));
                DragAndDrop.SetGenericData(SchematicTemplateGrid.DragDataKey, null);
            }
            evt.Use();
        }
    }
}
