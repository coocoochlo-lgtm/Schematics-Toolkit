# Int Tags

Int Tags replace Unity's string tags. A tag is an `int` id. In the Inspector and in Schematic graphs it shows as a
dropdown of names. Objects are filed in a `Dictionary<int, List<GameObject>>` under each of their tags, so
"every Enemy" is one dictionary lookup instead of a scene search, and an object can have as many tags as it needs.

```csharp
using Remedy.Framework;

public IntTag Target = IntTags.Enemy;           // shows as a dropdown in the Inspector

var player  = IntTagRegistry.FindFirst(IntTags.Player);
var enemies = IntTagRegistry.Find(IntTags.Enemy);      // IReadOnlyList<GameObject>; don't keep it or change it
if (other.gameObject.HasIntTag(IntTags.Player)) { }    // instead of CompareTag("Player")
IntTagRegistry.Added += (go, tag) => Debug.Log($"{go.name} is now {tag}");
```

## Giving objects tags

- **Schematic Objects** have a **Tags** list in their Inspector.
- **Any other GameObject**: add the **Int Tagged** component (`IntTagged`).
- **From code or graphs**: `IntTagRegistry.Add(go, tag)` / `Remove(go, tag)`, or the **Add Tag** / **Remove Tag** nodes.
  These tags last until the object is disabled or despawned. To tag an object permanently, add the tag to its Tags list.

Tags are registered while the component is enabled. Disabled objects, and pooled objects that are despawned, drop out
of lookups on their own. When a pooled object spawns again, it gets its Inspector tags back. Destroyed objects are
removed lazily, the next time their tag is looked up.

## Built-in tags

The ids are permanent. Never change them.

| id | name | | id | name |
|---|---|---|---|---|
| 0 | Untagged | | 6 | SpawnPoint |
| 1 | Player | | 7 | Vehicle |
| 2 | LevelDesign | | 8 | MainCamera |
| 3 | Enemy | | 9 | Respawn |
| 4 | Interactable | | 10 | Finish |
| 5 | Pickup | | | |

Ids below 100 are reserved for future built-ins.

## Your own tags

To add a tag, pick **Add Tag...** in any tag dropdown, type a name, and press Enter. The new tag gets the next free id
(100, 101, ...) and is selected for you. Put a `/` in a name to group tags into submenus, for example `Enemies/Boss`.

To rename or remove tags, use **Manage Tags...**, or open **Project Settings > Schematics > Int Tags**.
- Objects store the id, not the name, so renaming a tag changes nothing in your data.
- When you remove a tag, its id is never handed out again. Objects that still use it show `Missing Tag (id)` until
  you retag them.
- You can't rename or remove built-in tags.

Tag names are stored in `Resources/IntTagsData.asset`, which is created the first time you add a tag. Because it's in
`Resources`, builds can show tag names too (`IntTags.NameOf`, `IntTag.ToString()`). Commit this asset with the project.

## Graph nodes (Objects/Int Tags, Events/Int Tags)

| Node | What it does |
|---|---|
| Find With Tag | Outputs **Objects** (a list), **First** and **Count** for a tag. |
| Has Tag | Outputs whether an object has a tag. Turn on **Stop If Missing** to continue only if it does. |
| Add Tag / Remove Tag | Tags or untags an object. With nothing connected to **Object**, they use the object running the graph. |
| On Tag Added / On Tag Removed | Start a flow when an object gains or loses a tag. Set **Tag** to filter, or leave it Untagged to hear every tag. **Only This Object** limits it to the object running the graph. |

On a node, the Tag input is the same dropdown as in the Inspector, including **Add Tag...**. You can also connect a
value to it.

## Edit Mode

`IntTagRegistry` also works outside Play Mode. Whenever the open scenes (or the prefab being edited) change, the
editor marks the registry out of date. The next lookup rebuilds it from every enabled `IntTagged` and Schematic Object.
Tags added from code in Edit Mode are lost when it rebuilds. Use a component for tags you want to keep.

## Migrating from Unity tags

Run **Tools > Remedy > Schematics > Int Tags > Convert Unity Tags in Open Scenes**. Every object with a Unity tag gets
the matching Int Tag:
- Player, MainCamera, Respawn and Finish map to the built-ins.
- Any other tag becomes a user tag with the same name.

The Int Tag is added to the object's Schematic Object or IntTagged list. If the object has neither, an IntTagged
component is added. You can choose to keep or clear the Unity tags. MainCamera (which `Camera.main` needs) and
EditorOnly are always kept. To migrate code, replace `CompareTag("X")` with `HasIntTag(IntTags.X)`, and replace
`FindGameObjectsWithTag` with `IntTagRegistry.Find`.

## Files

- `IntTag.cs`: the struct (an int that converts to and from `int`).
- `IntTags.cs`: the built-in ids, plus `NameOf`, `All`, `TryGetId`, `IsDefault` and `Exists`.
- `IntTagsData.cs`: the asset that holds user tag names and allocates ids.
- `IntTagRegistry.cs`: the tag-to-objects dictionary and its events.
- `IntTagged.cs`, `IIntTagSource.cs`: components that give objects tags.
- `IntTagExtensions.cs`: `HasIntTag` and `GetIntTags` on GameObject.
- `Editor/`: the dropdown drawer, the Add Tag popup, the Project Settings page, the graph node field, the Edit Mode
  scanner and the migration tool. This folder is the Editor-only assembly `Remedy.Core.IntTags.Editor`.
