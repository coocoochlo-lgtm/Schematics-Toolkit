# Schematic Templates

Premade Schematic Objects you can start from, and the **Create New Schematic Object** browser that places them.

## Creating objects

- **Hierarchy**: right-click > *Schematics > Create New Schematic Object…* (also under the GameObject menu). Right-click
  an object and the new one goes under it.
- **Scene view**: right-click > *Create New Schematic Object…* (Unity 6's Scene view menu). The object lands on the surface
  you clicked (meshes are enough, no colliders needed), or the ground plane.
- **Project window**: right-click > *Create > Schematics > Schematic Object…*. You get a new prefab in that folder (a copy,
  or a Prefab Variant if you tick it in the browser's header) with **its own copy of the template's graph**, so editing it
  never changes the template. It's selected and ready to rename.
- You can also drag a tile from the browser straight into the Scene view.
- *Tools > Remedy > Schematics > Templates > Template Browser* opens the browser on its own.

Double-click or Enter picks a template. Right-click a tile for Open Prefab, Open Schematic Graph, Ping, Level Design,
Refresh Thumbnail and Remove From Templates. The old way (dragging a GameObject into the Project window) works as before.

## Adding your own templates

**From the browser:** click the **+** tile:

- *New Empty Template…*: a prefab with just a Schematic Object and a new graph.
- *New Template From Selection…*: saves the selected scene object as a template prefab (the scene object becomes an
  instance of it). If it already ran another prefab's graph, the template gets a copy.
- *Add Existing Prefab…*: lists a prefab you already have (and offers to add a Schematic Object if it has none).
- *New Catalog…*: makes another catalog (see below).

New templates go to `Assets/Content/Schematics/Templates/Custom` by default, in the selected category (or *Custom*).

**By hand:** select a catalog asset and add an entry to *Templates*: the prefab, a display name (empty uses the prefab
name), a category (the browser's chips), a description (the tooltip) and *Is Level Design*.

## Catalogs

A catalog is a `SchematicTemplatesData` asset (*Create > Schematics > Templates Catalog*). **Every catalog in the project is
merged** into one list, lowest *Order* first, then by path; when two catalogs list the same prefab the first wins. So a
feature, a team or a level can ship its own catalog without touching anyone else's.

The default catalog (`Assets/Content/Schematics/Templates/Default Templates.asset`) is made the first time something needs
it, e.g. by *Tools > Remedy > Schematics > Templates > Generate Level Kit Templates*, which builds the Level Kit (doors,
levers, buttons, pressure plates, coins and a counter, key and lock, checkpoint, spawner, moving platform, elevator) under
`Templates/Level Kit/` and lists them there. Running it again only re-lists them; *Rebuild Level Kit Templates* overwrites
them in place.

*Is Level Design* and the **Level Design Int Tag** on the prefab's Schematic Object are kept in step: tick one and the other
follows, so the Level Designer tools can find these objects in scenes too.

## Thumbnails

Rendered from a 3/4 angle that frames the prefab (like the Prefab view), one per editor tick, cached in memory and in
`Library/SchematicTemplateThumbnails` (keyed by the prefab's dependency hash, so they re-render when it changes). Prefabs
with nothing to render use Unity's preview. *Tools > Remedy > Schematics > Templates > Clear Thumbnail Cache* starts over.

## For tool authors

Assembly `Remedy.Schematics.Templates.Editor`, namespace `Remedy.Schematics.Templates`:

```csharp
// The merged catalog
IReadOnlyList<SchematicTemplate> all = SchematicTemplates.All;
IReadOnlyList<string> categories = SchematicTemplates.Categories;
Texture thumb = SchematicTemplates.Thumbnail(prefab);           // see SchematicTemplateThumbnails.Updated
SchematicTemplates.Changed += ...;                               // any catalog/template change
SchematicTemplates.Add(prefab, name, category, description, isLevelDesign, catalog);
SchematicTemplates.IsLevelDesign(template); SchematicTemplates.SetLevelDesign(template, true);

// The browser, embeddable anywhere
var grid = new SchematicTemplateGrid("MyTool.Grid") { Filter = SchematicTemplates.IsLevelDesign };
grid.Picked += template => SchematicObjectFactory.InstantiateInScene(template, parent, point);
root.Add(grid);

// Making objects
SchematicObjectFactory.InstantiateInScene(template, parent, worldPoint);
SchematicObjectFactory.CreatePrefabFromTemplate(template, folder, asVariant);
SchematicObjectFactory.SaveAsSchematicPrefab(root, path);        // any GameObject -> Schematic prefab with its own graph
```
