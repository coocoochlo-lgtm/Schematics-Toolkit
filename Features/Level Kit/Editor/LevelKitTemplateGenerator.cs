using System;
using System.Collections.Generic;
using Box3D.Hybrid;
using Remedy.Framework;
using Remedy.Schematics;
using Remedy.Schematics.Templates;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Remedy.LevelKit.Editor
{
    /// <summary>
    /// Tools > Remedy > Schematics > Templates > Generate Level Kit Templates: builds a ready-to-place prefab for each
    /// Level Kit Behavior (simple primitive looks, sensible sizes, Box3D shapes where things should be solid), each a
    /// Schematic Object with its own graph, tagged Level Design, and lists them in the default templates catalog.
    /// </summary>
    /// <remarks>
    /// Safe to run again: prefabs that already exist are left alone (only re-listed and re-tagged), so your edits to
    /// them survive. "Rebuild Level Kit Templates" overwrites them in place instead (same GUIDs, same graphs).
    /// </remarks>
    internal static class LevelKitTemplateGenerator
    {
        public const string Folder = SchematicObjectFactory.TemplatesFolder + "/Level Kit";
        private const string MaterialsFolder = Folder + "/Materials";
        private const string Category = "Level Kit";

        private static readonly Type[] BehaviorTypes =
        {
            typeof(Door), typeof(Switch), typeof(PressurePlate), typeof(Collectible), typeof(Counter),
            typeof(KeyItem), typeof(KeyLock), typeof(Checkpoint), typeof(Spawner), typeof(MovingPlatform),
        };

        private sealed class Item
        {
            public string Name;
            public string Description;
            public Func<GameObject> Build;
        }

        [MenuItem("Tools/Remedy/Schematics/Templates/Generate Level Kit Templates", false, 20)]
        private static void Generate() => Run(rebuild: false);

        [MenuItem("Tools/Remedy/Schematics/Templates/Rebuild Level Kit Templates", false, 21)]
        private static void Rebuild()
        {
            if (EditorUtility.DisplayDialog("Rebuild Level Kit Templates",
                    $"Rebuild every Level Kit prefab in {Folder}? Changes you made to them are replaced (their graphs are kept).", "Rebuild", "Cancel"))
                Run(rebuild: true);
        }

        /// <summary>Builds (or, with <paramref name="rebuild"/>, rebuilds) the Level Kit templates. Returns how many prefabs were written.</summary>
        public static int Run(bool rebuild)
        {
            foreach (var type in BehaviorTypes)
            {
                if (BehaviorMetadataGenerator.GetOrCreate(type) == null)
                {
                    Debug.LogError($"[Level Kit] Couldn't create the Behavior Metadata for {type.Name}. Try Tools/Remedy/Schematics/Refresh Behavior Metadata.");
                    return 0;
                }
            }

            SchematicObjectFactory.EnsureFolder(Folder);
            var catalog = SchematicTemplates.DefaultCatalog;
            int written = 0;

            try
            {
                var items = Items();
                for (int i = 0; i < items.Count; i++)
                {
                    var item = items[i];
                    EditorUtility.DisplayProgressBar("Level Kit Templates", item.Name, (float)i / items.Count);
                    string path = $"{Folder}/{item.Name}.prefab";
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);

                    if (prefab == null || rebuild)
                    {
                        var root = item.Build();
                        try
                        {
                            var schematic = root.GetComponent<SchematicObject>();
                            schematic.Tags ??= new List<IntTag>();
                            if (!schematic.Tags.Contains(new IntTag(IntTags.LevelDesign)))
                                schematic.Tags.Add(new IntTag(IntTags.LevelDesign));
                            prefab = SchematicObjectFactory.SaveAsSchematicPrefab(root, path);
                            if (prefab != null) written++;
                        }
                        finally
                        {
                            Object.DestroyImmediate(root);
                        }
                    }

                    if (prefab != null)
                        SchematicTemplates.Add(prefab, item.Name, Category, item.Description, isLevelDesign: true, catalog: catalog);
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            AssetDatabase.SaveAssets();
            SchematicTemplates.Refresh();
            Debug.Log($"[Level Kit] {written} template prefab(s) written to {Folder}; all Level Kit templates are listed in {AssetDatabase.GetAssetPath(catalog)}.", catalog);
            return written;
        }

        private static List<Item> Items() => new()
        {
            new Item { Name = "Door", Description = "A swinging door with a frame. Open, Close, Toggle and Use Signals; can auto-open for the player.", Build = () => BuildDoor("Door", DoorMotion.Swing, locked: false) },
            new Item { Name = "Sliding Door", Description = "Slides aside when near the player (auto-open), closes again after 2 s.", Build = () => BuildDoor("Sliding Door", DoorMotion.Slide, locked: false) },
            new Item { Name = "Locked Door", Description = "A door that needs the Red Key: Use it while carrying the key, or walk up with it.", Build = () => BuildDoor("Locked Door", DoorMotion.Swing, locked: true) },
            new Item { Name = "Lever", Description = "A toggle lever. Send Activate when the player interacts; wire On Turned On/Off to doors or platforms.", Build = BuildLever },
            new Item { Name = "Button", Description = "A pedestal button the player presses by walking up to it; on for one second.", Build = BuildButton },
            new Item { Name = "Pressure Plate", Description = "Pressed while anything with a physics body stands on it.", Build = BuildPressurePlate },
            new Item { Name = "Coin", Description = "A spinning coin that adds 1 to the 'Coins' Counter.", Build = BuildCoin },
            new Item { Name = "Coin Counter", Description = "Counts 'Coins' up to 10 and sends On Complete. Invisible in game.", Build = BuildCounter },
            new Item { Name = "Red Key", Description = "Gives the 'Red Key' to whoever picks it up.", Build = BuildKey },
            new Item { Name = "Key Lock", Description = "Unlocks when someone with the 'Red Key' comes close; wire On Unlocked to a door.", Build = BuildKeyLock },
            new Item { Name = "Checkpoint", Description = "Saves the player's respawn point when they walk through; the flag turns green.", Build = BuildCheckpoint },
            new Item { Name = "Spawner", Description = "Spawns its Prefab every 3 s, up to 3 at a time. Assign the Prefab.", Build = BuildSpawner },
            new Item { Name = "Moving Platform", Description = "Travels 6 m back and forth, carrying whatever stands on it.", Build = () => BuildPlatform("Moving Platform", new[] { Vector3.zero, new Vector3(0f, 0f, 6f) }) },
            new Item { Name = "Elevator", Description = "A lift that goes 4 m up and back down, pausing at each end.", Build = () => BuildPlatform("Elevator", new[] { Vector3.zero, new Vector3(0f, 4f, 0f) }) },
        };

        #region Builders

        private static GameObject BuildDoor(string name, DoorMotion motion, bool locked)
        {
            var root = Root(name, out var schematic);
            var frame = Mat("Frame", new Color(0.35f, 0.33f, 0.31f));
            var panelColor = locked ? new Color(0.75f, 0.2f, 0.18f) : motion == DoorMotion.Slide ? new Color(0.55f, 0.65f, 0.75f) : new Color(0.55f, 0.4f, 0.25f);

            Block(root.transform, "Post L", new Vector3(-0.85f, 1.15f, 0f), new Vector3(0.2f, 2.3f, 0.3f), frame, solid: true);
            Block(root.transform, "Post R", new Vector3(0.85f, 1.15f, 0f), new Vector3(0.2f, 2.3f, 0.3f), frame, solid: true);
            Block(root.transform, "Lintel", new Vector3(0f, 2.4f, 0f), new Vector3(1.9f, 0.2f, 0.3f), frame, solid: true);

            // Swinging: pivot on the hinge (left post). Sliding: pivot in the middle.
            var pivot = new GameObject("Panel Pivot");
            pivot.transform.SetParent(root.transform, false);
            pivot.transform.localPosition = motion == DoorMotion.Swing ? new Vector3(-0.75f, 0f, 0f) : Vector3.zero;
            Kinematic(pivot);
            var panelOffset = motion == DoorMotion.Swing ? new Vector3(0.75f, 1.1f, 0f) : new Vector3(0f, 1.1f, 0f);
            Block(pivot.transform, "Panel", panelOffset, new Vector3(1.5f, 2.2f, 0.1f), Mat(locked ? "Red" : motion == DoorMotion.Slide ? "Glass Blue" : "Wood", panelColor), solid: true);

            var door = schematic.AttachBehavior<Door>(root);
            door.SetOverride(nameof(Door.Panel), pivot.transform, typeof(Transform));
            door.SetOverride(nameof(Door.Motion), motion, typeof(DoorMotion));
            if (motion == DoorMotion.Slide)
            {
                door.SetOverride(nameof(Door.SlideOffset), new Vector3(1.55f, 0f, 0f), typeof(Vector3));
                door.SetOverride(nameof(Door.AutoOpenRadius), 2.5f, typeof(float));
                door.SetOverride(nameof(Door.AutoCloseDelay), 2f, typeof(float));
            }
            if (locked)
            {
                door.SetOverride(nameof(Door.IsLocked), true, typeof(bool));
                door.SetOverride(nameof(Door.RequiredKey), "Red Key", typeof(string));
                door.SetOverride(nameof(Door.AutoOpenRadius), 1.6f, typeof(float));
            }
            return root;
        }

        private static GameObject BuildLever()
        {
            var root = Root("Lever", out var schematic);
            Block(root.transform, "Base", new Vector3(0f, 0.1f, 0f), new Vector3(0.5f, 0.2f, 0.4f), Mat("Metal", new Color(0.4f, 0.42f, 0.45f)), solid: true);
            var pivot = new GameObject("Handle Pivot");
            pivot.transform.SetParent(root.transform, false);
            pivot.transform.localPosition = new Vector3(0f, 0.2f, 0f);
            Shape(PrimitiveType.Cylinder, pivot.transform, "Stick", new Vector3(0f, 0.3f, 0f), new Vector3(0.06f, 0.3f, 0.06f), Mat("Metal Dark", new Color(0.25f, 0.25f, 0.27f)), solid: false);
            Shape(PrimitiveType.Sphere, pivot.transform, "Knob", new Vector3(0f, 0.62f, 0f), Vector3.one * 0.14f, Mat("Red", new Color(0.75f, 0.2f, 0.18f)), solid: false);

            var lever = schematic.AttachBehavior<Switch>(root);
            lever.SetOverride(nameof(Switch.Handle), pivot.transform, typeof(Transform));
            lever.SetOverride(nameof(Switch.Mode), SwitchMode.Toggle, typeof(SwitchMode));
            lever.SetOverride(nameof(Switch.HandleMotion), SwitchHandleMotion.Rotate, typeof(SwitchHandleMotion));
            return root;
        }

        private static GameObject BuildButton()
        {
            var root = Root("Button", out var schematic);
            Shape(PrimitiveType.Cylinder, root.transform, "Pedestal", new Vector3(0f, 0.45f, 0f), new Vector3(0.45f, 0.45f, 0.45f), Mat("Metal", new Color(0.4f, 0.42f, 0.45f)), solid: true);
            var cap = Shape(PrimitiveType.Cylinder, root.transform, "Cap", new Vector3(0f, 0.93f, 0f), new Vector3(0.28f, 0.04f, 0.28f), Mat("Red", new Color(0.75f, 0.2f, 0.18f)), solid: false);

            var button = schematic.AttachBehavior<Switch>(root);
            button.SetOverride(nameof(Switch.Handle), cap.transform, typeof(Transform));
            button.SetOverride(nameof(Switch.Mode), SwitchMode.Momentary, typeof(SwitchMode));
            button.SetOverride(nameof(Switch.HandleMotion), SwitchHandleMotion.Press, typeof(SwitchHandleMotion));
            button.SetOverride(nameof(Switch.PressDepth), 0.03f, typeof(float));
            button.SetOverride(nameof(Switch.TouchRadius), 1.1f, typeof(float));
            return root;
        }

        private static GameObject BuildPressurePlate()
        {
            var root = Root("Pressure Plate", out var schematic);
            Block(root.transform, "Base", new Vector3(0f, 0.04f, 0f), new Vector3(1.6f, 0.08f, 1.6f), Mat("Metal", new Color(0.4f, 0.42f, 0.45f)), solid: true);
            var plate = Block(root.transform, "Plate", new Vector3(0f, 0.11f, 0f), new Vector3(1.3f, 0.06f, 1.3f), Mat("Amber", new Color(0.9f, 0.6f, 0.15f)), solid: false);

            var behavior = schematic.AttachBehavior<PressurePlate>(root);
            behavior.SetOverride(nameof(PressurePlate.Plate), plate.transform, typeof(Transform));
            return root;
        }

        private static GameObject BuildCoin()
        {
            var root = Root("Coin", out var schematic);
            var visual = new GameObject("Visual");
            visual.transform.SetParent(root.transform, false);
            visual.transform.localPosition = new Vector3(0f, 0.8f, 0f);
            var disc = Shape(PrimitiveType.Cylinder, visual.transform, "Disc", Vector3.zero, new Vector3(0.6f, 0.04f, 0.6f), Mat("Gold", new Color(1f, 0.8f, 0.2f)), solid: false);
            disc.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            var coin = schematic.AttachBehavior<Collectible>(root);
            coin.SetOverride(nameof(Collectible.Visual), visual.transform, typeof(Transform));
            return root;
        }

        private static GameObject BuildCounter()
        {
            var root = Root("Coin Counter", out var schematic);
            // A marker you can see and click in the editor; EditorOnly objects are stripped from builds.
            var marker = Shape(PrimitiveType.Sphere, root.transform, "Editor Marker", new Vector3(0f, 0.5f, 0f), Vector3.one * 0.5f, Mat("Gold", new Color(1f, 0.8f, 0.2f)), solid: false);
            marker.tag = "EditorOnly";
            schematic.AttachBehavior<Counter>(root);
            return root;
        }

        private static GameObject BuildKey()
        {
            var root = Root("Red Key", out var schematic);
            var visual = new GameObject("Visual");
            visual.transform.SetParent(root.transform, false);
            visual.transform.localPosition = new Vector3(0f, 0.9f, 0f);
            var red = Mat("Red", new Color(0.75f, 0.2f, 0.18f));
            var bow = Shape(PrimitiveType.Cylinder, visual.transform, "Bow", new Vector3(0f, 0.18f, 0f), new Vector3(0.26f, 0.03f, 0.26f), red, solid: false);
            bow.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            Block(visual.transform, "Shaft", new Vector3(0f, -0.1f, 0f), new Vector3(0.06f, 0.4f, 0.04f), red, solid: false);
            Block(visual.transform, "Bit", new Vector3(0.07f, -0.24f, 0f), new Vector3(0.1f, 0.08f, 0.04f), red, solid: false);

            var key = schematic.AttachBehavior<KeyItem>(root);
            key.SetOverride(nameof(KeyItem.Visual), visual.transform, typeof(Transform));
            key.SetOverride(nameof(KeyItem.KeyId), "Red Key", typeof(string));
            return root;
        }

        private static GameObject BuildKeyLock()
        {
            var root = Root("Key Lock", out var schematic);
            Block(root.transform, "Pillar", new Vector3(0f, 0.6f, 0f), new Vector3(0.5f, 1.2f, 0.5f), Mat("Metal", new Color(0.4f, 0.42f, 0.45f)), solid: true);
            Block(root.transform, "Keyhole Plate", new Vector3(0f, 1f, 0.26f), new Vector3(0.3f, 0.3f, 0.03f), Mat("Red", new Color(0.75f, 0.2f, 0.18f)), solid: false);
            var keyLock = schematic.AttachBehavior<KeyLock>(root);
            keyLock.SetOverride(nameof(KeyLock.KeyId), "Red Key", typeof(string));
            return root;
        }

        private static GameObject BuildCheckpoint()
        {
            var root = Root("Checkpoint", out var schematic);
            Shape(PrimitiveType.Cylinder, root.transform, "Base", new Vector3(0f, 0.03f, 0f), new Vector3(1.4f, 0.03f, 1.4f), Mat("Stone", new Color(0.55f, 0.55f, 0.52f)), solid: false);
            Shape(PrimitiveType.Cylinder, root.transform, "Pole", new Vector3(-0.5f, 1.1f, 0f), new Vector3(0.06f, 1.1f, 0.06f), Mat("Metal", new Color(0.4f, 0.42f, 0.45f)), solid: false);
            var flag = Block(root.transform, "Flag", new Vector3(-0.2f, 1.9f, 0f), new Vector3(0.6f, 0.4f, 0.03f), Mat("White", new Color(0.92f, 0.92f, 0.9f)), solid: false);

            var checkpoint = schematic.AttachBehavior<Checkpoint>(root);
            checkpoint.SetOverride(nameof(Checkpoint.Flag), flag.transform, typeof(Transform));
            return root;
        }

        private static GameObject BuildSpawner()
        {
            var root = Root("Spawner", out var schematic);
            Shape(PrimitiveType.Cylinder, root.transform, "Pad", new Vector3(0f, 0.03f, 0f), new Vector3(1.2f, 0.03f, 1.2f), Mat("Purple", new Color(0.55f, 0.35f, 0.8f)), solid: false);
            Block(root.transform, "Arrow", new Vector3(0f, 0.07f, 0.35f), new Vector3(0.15f, 0.02f, 0.4f), Mat("White", new Color(0.92f, 0.92f, 0.9f)), solid: false);
            schematic.AttachBehavior<Spawner>(root);
            return root;
        }

        private static GameObject BuildPlatform(string name, Vector3[] waypoints)
        {
            var root = Root(name, out var schematic);
            Kinematic(root);
            var size = new Vector3(3f, 0.3f, 3f);
            Block(root.transform, "Deck", Vector3.zero, size, Mat("Platform", new Color(0.3f, 0.55f, 0.7f)), solid: true);

            var platform = schematic.AttachBehavior<MovingPlatform>(root);
            platform.SetOverride(nameof(MovingPlatform.Waypoints), waypoints, typeof(Vector3[]));
            platform.SetOverride(nameof(MovingPlatform.RiderZoneSize), size, typeof(Vector3));
            return root;
        }

        #endregion

        #region Helpers

        private static GameObject Root(string name, out SchematicObject schematic)
        {
            var root = new GameObject(name);
            schematic = root.AddComponent<SchematicObject>();
            return root;
        }

        private static GameObject Block(Transform parent, string name, Vector3 position, Vector3 size, Material material, bool solid)
            => Shape(PrimitiveType.Cube, parent, name, position, size, material, solid);

        // A primitive without Unity's collider (the toolkit simulates with Box3D); solid ones get a Box3D box shape,
        // which scales with the Transform, so a unit box matches the primitive.
        private static GameObject Shape(PrimitiveType type, Transform parent, string name, Vector3 position, Vector3 scale, Material material, bool solid)
        {
            var go = GameObject.CreatePrimitive(type);
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localScale = scale;
            if (material != null)
                go.GetComponent<MeshRenderer>().sharedMaterial = material;
            if (solid)
                go.AddComponent<Box3DBoxShape>();
            return go;
        }

        private static void Kinematic(GameObject go)
        {
            if (!go.TryGetComponent(out Box3DBody body))
                body = go.AddComponent<Box3DBody>();
            var serialized = new SerializedObject(body);
            var type = serialized.FindProperty("Type");
            if (type != null)
            {
                type.enumValueIndex = (int)Box3DBodyType.Kinematic;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        // One material asset per colour, shared by every template (made on first use).
        private static Material Mat(string name, Color color)
        {
            SchematicObjectFactory.EnsureFolder(MaterialsFolder);
            string path = $"{MaterialsFolder}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null)
                return material;

            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            if (shader == null)
                return null;
            material = new Material(shader) { name = name };
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.25f);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        #endregion
    }
}
