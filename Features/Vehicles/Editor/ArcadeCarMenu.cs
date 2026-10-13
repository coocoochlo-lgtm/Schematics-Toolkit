using System.IO;
using Box3D.Hybrid;
using Remedy.Schematics;
using UnityEditor;
using UnityEngine;

namespace Remedy.Vehicles.Editor
{
    /// <summary>
    /// Tools > Remedy > Vehicles > Create Arcade Car: a ready-to-drive car prefab to start from. A Box3D body with a
    /// chassis and a cabin shape, Arcade Car with four wheels (placeholder cylinders), its Properties asset, and
    /// Vehicle Seats with a driver and a passenger seat (and their Signals). Made in the selected Project folder.
    /// </summary>
    internal static class ArcadeCarMenu
    {
        private const string DefaultFolder = "Assets/Content/Prefabs/Vehicles";

        [MenuItem("Tools/Remedy/Vehicles/Create Arcade Car")]
        private static void CreateArcadeCar()
        {
            // Attaching needs each Behavior's Metadata.
            if (BehaviorMetadataGenerator.GetOrCreate(typeof(ArcadeCar)) == null || BehaviorMetadataGenerator.GetOrCreate(typeof(VehicleSeats)) == null)
            {
                Debug.LogError("Couldn't create the Behavior Metadata for Arcade Car / Vehicle Seats. Try Tools/Remedy/Schematics/Refresh Behavior Metadata.");
                return;
            }

            string folder = SelectedFolder();
            if (!AssetDatabase.IsValidFolder(folder))
            {
                Directory.CreateDirectory(folder);
                AssetDatabase.Refresh();
            }
            string prefabPath = AssetDatabase.GenerateUniqueAssetPath($"{folder}/Arcade Car.prefab");
            string name = Path.GetFileNameWithoutExtension(prefabPath);

            var root = new GameObject(name);
            try
            {
                var schematic = root.AddComponent<SchematicObject>();
                root.AddComponent<Box3DBody>();

                // Arcade Car: body, chassis shape, impact relay and four wheel anchors (its OnSetup).
                var car = schematic.AttachBehavior<ArcadeCar>(root);
                var chassis = root.GetComponent<Box3DBoxShape>();
                SetSerialized(chassis, "Size", new Vector3(1.8f, 0.7f, 4f));

                // A cabin as a second shape on the same body, so the roof is there to land on.
                var cabin = new GameObject("Cabin");
                cabin.transform.SetParent(root.transform, false);
                cabin.transform.localPosition = new Vector3(0f, 0.6f, -0.2f);
                var cabinShape = cabin.AddComponent<Box3DBoxShape>();
                SetSerialized(cabinShape, "Size", new Vector3(1.5f, 0.55f, 2f));

                // Placeholder looks.
                var visuals = new GameObject("Visuals").transform;
                visuals.SetParent(root.transform, false);
                Block(visuals, "Body", Vector3.zero, new Vector3(1.8f, 0.7f, 4f));
                Block(visuals, "Cabin", cabin.transform.localPosition, new Vector3(1.5f, 0.55f, 2f));

                var wheels = car.FindOverride("Wheels")?.Value?.Get<Wheel[]>();
                if (wheels != null)
                {
                    foreach (var wheel in wheels)
                    {
                        if (wheel?.Anchor == null) continue;
                        var mesh = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                        Object.DestroyImmediate(mesh.GetComponent<Collider>());
                        mesh.name = "Visual";
                        mesh.transform.SetParent(wheel.Anchor, false);
                        mesh.transform.localPosition = new Vector3(0f, -0.3f, 0f);
                        mesh.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                        mesh.transform.localScale = new Vector3(wheel.Radius * 2f, 0.12f, wheel.Radius * 2f);
                        wheel.Visual = mesh.transform;
                    }
                    car.SetOverride("Wheels", wheels, typeof(Wheel[]));
                }

                // Its tuning, next to the prefab.
                var properties = ScriptableObject.CreateInstance<ArcadeCarProperties>();
                AssetDatabase.CreateAsset(properties, AssetDatabase.GenerateUniqueAssetPath($"{folder}/{name} Properties.asset"));
                car.SetOverride("Properties", properties, typeof(ArcadeCarProperties));

                // Seats: OnSetup made the driver's; give it Signals and add a passenger.
                schematic.AttachBehavior<VehicleSeats>(root);
                var scope = VehicleSeatEditing.Read(root, "");
                if (scope != null)
                {
                    scope.AssetPath = prefabPath;
                    scope.Live = false; // a temporary object: nothing to undo
                    foreach (var seat in scope.Seats)
                        VehicleSeatEditing.CreateSignals(scope, seat);
                    VehicleSeatEditing.AddSeat(scope, "Passenger");
                    scope.Commit();
                }

                var prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                if (prefab != null)
                {
                    var saved = prefab.GetComponent<SchematicObject>();
                    saved.AssignObjectID(SchematicObjectIDs.For(prefabPath));
                    EditorUtility.SetDirty(prefab);
                    AssetDatabase.SaveAssets();
                    Selection.activeObject = prefab;
                    EditorGUIUtility.PingObject(prefab);
                    Debug.Log($"Arcade Car created at {prefabPath}. Open it to place the seats and wheels; right-click its Properties for presets.", prefab);
                }
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static string SelectedFolder()
        {
            foreach (var guid in Selection.assetGUIDs)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetDatabase.IsValidFolder(path)) return path;
                if (!string.IsNullOrEmpty(path)) return Path.GetDirectoryName(path)?.Replace('\\', '/');
            }
            return DefaultFolder;
        }

        private static void SetSerialized(Object target, string property, Vector3 value)
        {
            if (target == null) return;
            var serialized = new SerializedObject(target);
            var field = serialized.FindProperty(property);
            if (field == null) return;
            field.vector3Value = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void Block(Transform parent, string name, Vector3 position, Vector3 size)
        {
            var block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(block.GetComponent<Collider>());
            block.name = name;
            block.transform.SetParent(parent, false);
            block.transform.localPosition = position;
            block.transform.localScale = size;
        }
    }
}
