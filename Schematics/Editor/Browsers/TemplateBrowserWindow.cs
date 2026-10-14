using System;
using Remedy.Schematics.Templates;
using UnityEditor;
using UnityEngine.UIElements;

namespace Remedy.Schematics.Browsers
{
    /// <summary>
    /// The Template Browser as a normal dockable tab: every Schematic template (all catalogs merged), to look through,
    /// manage (the "+" tile and the right-click menu) and place. Double-click or Enter puts the template in the open
    /// scene at the Scene view's pivot; drag a tile into the Scene view to put it where you drop it.
    /// </summary>
    /// <remarks>
    /// Opened from Window > Remedy > Template Browser or the main toolbar. Right-click creation (Hierarchy, Scene view,
    /// Project window) still uses the small <see cref="CreateSchematicObjectWindow"/> pop-up, which shows the same grid.
    /// </remarks>
    public class TemplateBrowserWindow : SchematicBrowserWindow
    {
        /// <summary>Opens (or focuses) the Template Browser.</summary>
        public static TemplateBrowserWindow Open() => OpenWindow<TemplateBrowserWindow>();

        protected override string PrefsKey => "Remedy.Schematics.TemplateBrowser";
        protected override string Title => "Templates";
        protected override string IconName => "Prefab Icon";
        protected override string Hint => "Double-click to place in the scene, or drag into the Scene view.";

        protected override void BuildHeader(VisualElement header)
        {
            header.Add(HeaderToggle("Level Design only", "Only show level design building blocks (templates tagged Level Design).",
                "LevelDesignOnly", false, ApplyFilter));

            var newPrefab = new Button(() => CreateSchematicObjectWindow.ShowForProject(SchematicObjectFactory.SelectedProjectFolder()))
            {
                text = "New Prefab…",
                tooltip = "Make a new prefab of your own from a template, in the folder selected in the Project window.",
            };
            header.Add(newPrefab);
        }

        protected override void ConfigureGrid(SchematicTemplateGrid grid)
        {
            ApplyFilter(EditorPrefs.GetBool($"{PrefsKey}.LevelDesignOnly", false));
        }

        private void ApplyFilter(bool levelDesignOnly)
        {
            if (Grid == null) return;
            Grid.Filter = levelDesignOnly ? new Func<SchematicTemplate, bool>(SchematicTemplates.IsLevelDesign) : null;
            Grid.NewTemplatesAreLevelDesign = levelDesignOnly;
        }

        protected override void OnPicked(SchematicTemplate template)
        {
            if (template?.Prefab != null)
                SchematicObjectFactory.InstantiateInScene(template);
        }
    }
}
