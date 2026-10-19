using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Remedy.Schematics.Browsers
{
    /// <summary>A dockable home for the Schematics asset browsers and the Level Designer.</summary>
    public class ProjectManagerWindow : EditorWindow
    {
        public enum Section { SchematicObjects = 1, Audio = 2, LevelDesigner = 3 }

        private readonly Dictionary<Section, EditorWindow> _pages = new();
        private readonly Dictionary<Section, VisualElement> _pageRoots = new();
        private readonly Dictionary<Section, ToolbarToggle> _tabToggles = new();
        private VisualElement _content;
        private bool _levelDesignerPaletteRequested;

        [SerializeField] private Section _section;

        internal SchematicObjectsBrowserWindow ObjectsPage => Page<SchematicObjectsBrowserWindow>(Section.SchematicObjects);

        [MenuItem("Window/Remedy/Project Manager", false, 99)]
        [MenuItem("Tools/Remedy/Project Manager", false, 0)]
        public static void Open() => Open(Section.SchematicObjects);

        public static ProjectManagerWindow Open(Section section)
        {
            var window = GetWindow<ProjectManagerWindow>(false, "Project Manager", true);
            window.titleContent = new GUIContent("Project Manager", SchematicBrowsers.Icon("Project"));
            window.minSize = new Vector2(420f, 250f);
            window.Show();
            window.Focus();
            window.SelectSection(section);
            return window;
        }

        public static void OpenLevelDesigner() => Open(Section.LevelDesigner);
        public static void OpenLevelDesignerPalette()
        {
            var window = Open(Section.LevelDesigner);
            window._levelDesignerPaletteRequested = true;
            window.SelectLevelDesignerPalette();
        }

        private void OnEnable()
        {
            titleContent = new GUIContent("Project Manager", SchematicBrowsers.Icon("Project"));
            minSize = new Vector2(420f, 250f);
        }

        private void OnDisable()
        {
            foreach (var page in _pages.Values)
                if (page != null)
                    DestroyImmediate(page);
            _pages.Clear();
            _pageRoots.Clear();
            _tabToggles.Clear();
        }

        public void CreateGUI()
        {
            var root = rootVisualElement;
            root.Clear();
            _pageRoots.Clear();
            _tabToggles.Clear();
            if (!Enum.IsDefined(typeof(Section), _section))
                _section = Section.SchematicObjects;
            root.AddToClassList("project-manager");
            var sheet = SchematicBrowserWindow.StyleSheet;
            if (sheet != null)
                root.styleSheets.Add(sheet);

            var toolbar = new Toolbar();
            toolbar.AddToClassList("project-manager__tabs");
            AddTab(toolbar, Section.SchematicObjects, "Schematics");
            AddTab(toolbar, Section.Audio, "Audio");
            if (SchematicBrowsers.HasLevelDesigner)
                AddTab(toolbar, Section.LevelDesigner, "Levels");
            root.Add(toolbar);

            _content = new VisualElement();
            _content.AddToClassList("project-manager__content");
            root.Add(_content);
            SelectSection(_section);
        }

        private void AddTab(VisualElement toolbar, Section section, string label)
        {
            var toggle = new ToolbarToggle { text = label, value = section == _section };
            toggle.AddToClassList("project-manager__tab");
            toggle.RegisterValueChangedCallback(evt =>
            {
                if (evt.newValue)
                    SelectSection(section);
            });
            _tabToggles[section] = toggle;
            toolbar.Add(toggle);
        }

        private void SelectSection(Section section)
        {
            if (_section == Section.Audio && section != Section.Audio
                && _pages.TryGetValue(Section.Audio, out var audioPage)
                && audioPage is AudioClipBrowserWindow audioBrowser)
                audioBrowser.StopHostedPreview();

            _section = section;
            foreach (var pair in _tabToggles)
                pair.Value.SetValueWithoutNotify(pair.Key == section);
            if (_content == null)
                return;

            var page = GetPage(section);
            foreach (var pair in _pageRoots)
                pair.Value.style.display = pair.Key == section ? DisplayStyle.Flex : DisplayStyle.None;
            if (page == null)
            {
                _content.Clear();
                var message = new Label("The Level Designer feature is not installed.");
                message.AddToClassList("project-manager__empty");
                _content.Add(message);
                return;
            }
            if (!_pageRoots.TryGetValue(section, out var pageRoot))
            {
                pageRoot = new VisualElement();
                pageRoot.AddToClassList("project-manager__page");
                _pageRoots.Add(section, pageRoot);
                _content.Add(pageRoot);
                BuildPage(page, section, pageRoot);
            }
            pageRoot.style.display = DisplayStyle.Flex;
        }

        private void SelectLevelDesignerPalette()
        {
            _levelDesignerPaletteRequested = true;
            var page = GetPage(Section.LevelDesigner);
            var method = page?.GetType().GetMethod("CreateHostedGUI", BindingFlags.Public | BindingFlags.Instance);
            var root = _pageRoots.TryGetValue(Section.LevelDesigner, out var pageRoot) ? pageRoot : null;
            if (method != null && root != null)
            {
                method.Invoke(page, new object[] { root, true });
                _levelDesignerPaletteRequested = false;
            }
        }

        private EditorWindow GetPage(Section section)
        {
            if (_pages.TryGetValue(section, out var existing) && existing != null)
                return existing;

            EditorWindow page = section switch
            {
                Section.SchematicObjects => CreateInstance<SchematicObjectsBrowserWindow>(),
                Section.Audio => CreateInstance<AudioClipBrowserWindow>(),
                Section.LevelDesigner => SchematicBrowsers.LevelDesignerType != null
                    ? CreateInstance(SchematicBrowsers.LevelDesignerType) as EditorWindow
                    : null,
                _ => null
            };
            if (page != null)
                _pages[section] = page;
            return page;
        }

        private void BuildPage(EditorWindow page, Section section, VisualElement host)
        {
            if (page is SchematicBrowserWindow browser)
            {
                browser.BuildHostedUI(host);
                return;
            }

            var build = page.GetType().GetMethod("CreateHostedGUI", BindingFlags.Public | BindingFlags.Instance);
            if (build != null)
            {
                bool showPalette = section == Section.LevelDesigner && _levelDesignerPaletteRequested;
                build.Invoke(page, new object[] { host, showPalette });
                _levelDesignerPaletteRequested = false;
            }
            else
                host.Add(new Label("This section cannot be hosted in Project Manager."));
        }

        private T Page<T>(Section section) where T : EditorWindow => GetPage(section) as T;
    }
}
