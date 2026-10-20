using Remedy.Schematics.Templates;

namespace Remedy.Schematics.Browsers
{
    /// <summary>
    /// Legacy entry point for the template chooser. Templates are no longer a dockable browser; choosing one opens
    /// the create-object window, where the selected template is used to make a new Schematic Object prefab.
    /// </summary>
    public static class TemplateBrowserWindow
    {
        /// <summary>Opens the template chooser for a new prefab in the selected Project window folder.</summary>
        public static CreateSchematicObjectWindow Open()
            => CreateSchematicObjectWindow.ShowForProject(SchematicObjectFactory.SelectedProjectFolder());
    }
}
