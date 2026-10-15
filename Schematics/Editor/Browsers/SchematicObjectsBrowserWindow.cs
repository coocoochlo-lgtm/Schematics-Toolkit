using System;
using System.Collections.Generic;
using Remedy.Schematics.Templates;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Remedy.Schematics.Browsers
{
    /// <summary>
    /// The Schematic Objects section hosted in Project Manager: every Schematic prefab already in the project (any prefab under Assets/ with a Schematic Object
    /// on its root), in the same grid as the Template Browser. Chips group them by folder or by Int Tag. Double-click
    /// opens the prefab; drag a tile into the Scene view to place it; right-click to open its graph, ping it, make it a
    /// template or tag it for level design.
    /// </summary>
    /// <remarks>
    /// The list comes from <see cref="SchematicObjectIndex"/>, which scans once and then follows imports, so it stays
    /// current without rescanning the project on every change.
    /// </remarks>
    public class SchematicObjectsBrowserWindow : SchematicBrowserWindow
    {
        private enum GroupBy { Folder, Tag }

        private static readonly List<string> GroupChoices = new() { "Group by Folder", "Group by Tag" };

        /// <summary>Opens Project Manager on the Schematic Objects section.</summary>
        public static SchematicObjectsBrowserWindow Open() => ProjectManagerWindow.Open(ProjectManagerWindow.Section.SchematicObjects).ObjectsPage;

        protected override string PrefsKey => "Remedy.Schematics.ObjectsBrowser";
        protected override string Title => "Schematic Objects";
        protected override string IconName => "PrefabVariant Icon";
        protected override string Hint => "Double-click to open, or drag into the Scene view.";

        private GroupBy Grouping
        {
            get => (GroupBy)EditorPrefs.GetInt($"{PrefsKey}.GroupBy", 0);
            set => EditorPrefs.SetInt($"{PrefsKey}.GroupBy", (int)value);
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            SchematicObjectIndex.Changed += OnIndexChanged;
        }

        protected override void OnDisable()
        {
            SchematicObjectIndex.Changed -= OnIndexChanged;
            base.OnDisable();
        }

        private void OnIndexChanged() => Grid?.schedule.Execute(() => Grid?.Refresh());

        protected override void BuildHeader(VisualElement header)
        {
            var group = new DropdownField(GroupChoices, (int)Grouping) { tooltip = "What the chips group the objects by." };
            group.AddToClassList("schematic-browser__dropdown");
            group.RegisterValueChangedCallback(_ =>
            {
                Grouping = (GroupBy)Mathf.Max(0, GroupChoices.IndexOf(group.value));
                ApplyGrouping();
            });
            header.Add(group);

            header.Add(HeaderToggle("Level Design only", "Only show objects tagged Level Design.", "LevelDesignOnly", false, ApplyFilter));

            header.Add(new Button(SchematicObjectIndex.Rescan)
            {
                text = "Rescan",
                tooltip = "Scan the whole project for Schematic prefabs again (it normally keeps up by itself).",
            });
        }

        protected override void ConfigureGrid(SchematicTemplateGrid grid)
        {
            grid.ShowCreateTile = true;
            grid.CreateTileLabel = "New Schematic Object…";
            grid.CreateTileTooltip = "Create a Schematic Object from an empty template, the selected scene object, or a prefab.";
            grid.EmptyMessage = "No Schematic Objects yet.\nMake one from the Template Browser, or right-click in the Project window > Create > Schematics > Schematic Object.";
            grid.TileMenu = BuildMenu;
            grid.AdditionalTileMenu = (menu, entry) =>
                ProjectManagerTags.AppendMenu(menu, "SchematicObjects", entry.Prefab, () => Grid?.Refresh());
            grid.Source = () => SchematicObjectIndex.All;
            ApplyGrouping();
            ApplyFilter(EditorPrefs.GetBool($"{PrefsKey}.LevelDesignOnly", false));
        }

        private void ApplyGrouping()
        {
            if (Grid == null) return;
            Grid.CategoriesOf = Grouping == GroupBy.Tag
                ? new Func<SchematicTemplate, IEnumerable<string>>(entry =>
                    ProjectManagerTags.Categories("SchematicObjects", entry.Prefab, SchematicObjectIndex.TagsOf(entry)))
                : new Func<SchematicTemplate, IEnumerable<string>>(entry =>
                    ProjectManagerTags.Categories("SchematicObjects", entry.Prefab, new[] { SchematicObjectIndex.FolderOf(entry) }));
        }

        private void ApplyFilter(bool levelDesignOnly)
        {
            if (Grid == null) return;
            Grid.Filter = levelDesignOnly ? new Func<SchematicTemplate, bool>(entry => entry.IsLevelDesign) : null;
        }

        protected override void OnPicked(SchematicTemplate entry) => OpenPrefab(entry?.Prefab);

        private void BuildMenu(DropdownMenu menu, SchematicTemplate entry)
        {
            var prefab = entry.Prefab;
            if (prefab == null) return;

            menu.AppendAction("Open Prefab", _ => OpenPrefab(prefab));
            menu.AppendAction("Open Schematic Graph", _ => SchematicObjectFactory.OpenGraph(prefab),
                SchematicObjectFactory.GraphOf(prefab) != null ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
            menu.AppendAction("Ping in Project", _ => Ping(prefab));
            menu.AppendAction("Place in Scene", _ => SchematicObjectFactory.InstantiateInScene(entry));
            menu.AppendSeparator();

            var template = SchematicTemplates.Find(prefab);
            if (template == null)
                menu.AppendAction("Make Template", _ => MakeTemplate(prefab));
            else
                menu.AppendAction("Show Template in Catalog", _ => Ping(SchematicTemplates.CatalogOf(template)));

            bool levelDesign = template != null ? SchematicTemplates.IsLevelDesign(template) : SchematicTemplates.HasLevelDesignTag(prefab);
            menu.AppendAction("Level Design", _ => SetLevelDesign(prefab, !levelDesign),
                levelDesign ? DropdownMenuAction.Status.Checked : DropdownMenuAction.Status.Normal);
            menu.AppendAction("Refresh Thumbnail", _ => SchematicTemplateThumbnails.Invalidate(prefab));
        }

        // Lists the prefab in the default catalog (under the default category), so it shows up in the Template Browser.
        private static void MakeTemplate(GameObject prefab)
        {
            var template = SchematicTemplates.Add(prefab, null, SchematicTemplates.DefaultCategory);
            if (template != null)
                Debug.Log($"[Schematics] '{prefab.name}' is now a template (in {AssetDatabase.GetAssetPath(SchematicTemplates.CatalogOf(template))}).", prefab);
        }

        // The Level Design Int Tag on the prefab, and the template's flag too when it is one, kept in step.
        private static void SetLevelDesign(GameObject prefab, bool value)
        {
            var template = SchematicTemplates.Find(prefab);
            if (template != null)
                SchematicTemplates.SetLevelDesign(template, value);
            else
                SchematicTemplates.SetPrefabLevelDesignTag(prefab, value);
            SchematicObjectIndex.Refresh(prefab);
        }
    }
}
