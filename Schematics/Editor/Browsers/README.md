# Schematics Browsers

Dockable UI Toolkit windows for finding and managing Schematic Objects and audio. The Schematic Object browsers share
one look: a header (a hint, the window's own controls, and buttons to jump between browsers and the Level Designer)
over the thumbnail grid (search, size slider, chips, tiles) from `Schematics/Editor/Templates`.

| Window | Open from | Double-click | Right-click |
| --- | --- | --- | --- |
| **Templates** | *Window > Remedy > Template Browser*, main toolbar | places it in the scene | Create, Open Prefab, Open Graph, Ping, Level Design, Remove… |
| **Schematic Objects** | *Window > Remedy > Schematic Objects*, main toolbar | opens the prefab | Open Prefab, Open Graph, Ping, Place in Scene, Make Template, Level Design, Refresh Thumbnail |
| **Audio Clips** | *Window > Remedy > Audio Clip Browser*, or the **Audio** browser tab | click a waveform to preview; drag it to use the clip | set one tag, move between Music/SFX, select in Project |

Drag any tile into the Scene view to place it where you drop it. Schematic Objects lists every prefab under `Assets/`
with a Schematic Object on its root; the chips group them by folder or by Int Tag (header dropdown). The list is scanned
once and then follows imports, moves and deletes (*Rescan* starts over).

The Audio Clip Browser manages `Assets/Content/Audio/Music`, `Assets/Content/Audio/SFX`, and its editable tags under
`Assets/Content/Audio/Tags`. It imports files or folder trees, creates folders, previews one clip at a time, and lets
you seek on the clip timeline above its waveform. Each clip has at most one tag; tags carry their own color and
Music/SFX category. Assigning a tag moves the clip to that category, and the audio catalog is rebuilt while preserving
existing per-clip settings.

## Main toolbar

**Level Designer**, **Templates** and **Objects** buttons sit on the main toolbar, right of the Play buttons.

- Unity 6.3+: Unity's `[MainToolbarElement]` API, docked right. If you don't see them, right-click the main toolbar and
  turn on the *Remedy* elements (you can move them there too).
- Unity 6.0 to 6.2: injected into the toolbar's right-hand zone by reflection. If that ever fails you get one Console
  message and the windows are still under *Window > Remedy*.

The Level Designer is opened by type name (`Remedy.LevelDesigner.Editor.LevelDesignerWindow`), so this assembly doesn't
depend on it; without it, its button is left out.

## Making another browser

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

Restyle every browser at once in `SchematicBrowser.uss` (class names are listed at its top).
For content that is not a schematic-template grid, override `BuildContent()` and keep the shared header/navigation.
