# Project Manager

Project Manager is a dockable window containing tabs for **Schematic Objects**, **Audio**, and (when installed) the
**Level Designer**. The Templates shortcut opens a focused, non-dockable creation window instead of a browser tab.
Choose an existing template, or use the plus tile to create an empty template, add a template from a selected scene
object, or load an existing prefab as a template. Select a template and press **Okay** to create a new Schematic Object
prefab in the selected Project window folder.

Schematic Objects retains the asset grid, with a plus tile that opens the template chooser to create a new object.
Tiles highlight on hover and briefly flash on activation. Tag definitions are stored in the project asset at
`Assets/Project Manager/ProjectManagerTags.asset`, while assignments remain asset labels. Legacy per-user tag names
are migrated into that asset when a section is next opened. Tags can be assigned from a tile's context menu and are
also available as grid chips. Audio retains its separate `AudioClipTag` assets,
Music/SFX organization, import, waveform preview/seeking and per-clip tag behavior. Its play gesture toggles playback,
shows a fading play cue, and briefly flashes the whole card.

The Level Designer remains optional: Project Manager discovers it by type name and does not reference its assembly.
Its existing Levels, Characters and Palette tabs are hosted within the Level Designer section. Tags there attach to
saved blockout/variant scene assets, never objects inside a scene.

The Audio section manages `Assets/Content/Audio/Music`, `Assets/Content/Audio/SFX`, and tags under
`Assets/Content/Audio/Tags`. Each audio clip has at most one Audio tag; tags carry a color and Music/SFX category.
Assigning a tag moves the clip to that category, and the audio catalog rebuild preserves existing per-clip settings.

## Main toolbar

The Project Manager and section shortcuts appear on Unity's main toolbar.

- Unity 6.3+: Unity's `[MainToolbarElement]` API. Right-click the toolbar to show or move the *Remedy* elements.
- Unity 6.0 to 6.2: the buttons are injected into the toolbar's right-hand zone by reflection. If that changes in a
  Unity update, they remain available under *Window > Remedy*.

## Extending a browser section

```csharp
public class MyBrowser : SchematicBrowserWindow
{
    public static MyBrowser Open() => OpenWindow<MyBrowser>();
    protected override string PrefsKey => "MyTool.Browser";
    protected override string Title => "My Things";
    protected override void ConfigureGrid(SchematicTemplateGrid grid) { grid.Source = () => MyIndex.All; grid.ShowCreateTile = false; }
    protected override void BuildHeader(VisualElement header) { header.Add(HeaderToggle("Only mine", "", "OnlyMine", false, on => { })); }
    protected override void OnPicked(SchematicTemplate item) => AssetDatabase.OpenAsset(item.Prefab);
}
```

Restyle the shared browser content in `SchematicBrowser.uss`. For content that is not a schematic-template grid,
override `BuildContent()` and `BuildHeader()` on `SchematicBrowserWindow`; Project Manager hosts those same panels.
