using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Box3D.Hybrid;
using Remedy.CharacterControllers.Hover;
using Remedy.Schematics;
using UnityEditor;
using UnityEngine;

namespace Remedy.LevelDesigner.Editor
{
    /// <summary>
    /// Reads a character prefab's movement tuning (the Physics Based Character Controller's Properties, plus the
    /// Hover Controller's if it has one) and turns it into <see cref="CharacterMetrics"/>: run speed, jump height,
    /// time in the air, jump distance and so on. The numbers follow how the controller actually moves:
    /// <list type="bullet">
    /// <item>A jump rises at Jump Speed until the body is Jump Height above the ground (it rides Ride Height above it
    /// when standing, so the feet rise Jump Height - Ride Height), then holds for Hang Time.</item>
    /// <item>The fall adds Fall Speed to the downward velocity every physics step, on top of gravity, so it falls at
    /// gravity + Fall Speed / fixed time step.</item>
    /// <item>Running speeds up at Acceleration and slows at Deceleration (both per second) toward Move Speed, in the air too.</item>
    /// </list>
    /// They're predictions (springs and collisions make the real thing a little softer), good for laying out a level
    /// and for animators timing jump animations.
    /// </summary>
    public static class CharacterMetricsAnalyzer
    {
        /// <summary>True if the prefab has the toolkit's character controller.</summary>
        public static bool IsCharacter(GameObject prefab) => prefab != null && FindAttachment(prefab, typeof(PhysicsBasedCharacterController)) != null;

        /// <summary>Works out a character prefab's metrics. False if it has no character controller with Properties.</summary>
        public static bool TryAnalyze(GameObject prefab, out CharacterMetrics metrics)
        {
            metrics = default;
            if (prefab == null)
                return false;

            var properties = ReadVariable<CharacterControllerProperties>(prefab, typeof(PhysicsBasedCharacterController), "Properties");
            if (properties == null)
                return false;

            float dt = Mathf.Max(0.001f, Time.fixedDeltaTime);
            float gravity = WorldGravity();

            metrics.CharacterName = prefab.name;
            metrics.Gravity = gravity;
            metrics.FixedTimeStep = dt;

            // Ground movement.
            metrics.MoveSpeed = Mathf.Max(0f, properties.MoveSpeed);
            metrics.Acceleration = Mathf.Max(0f, properties.Acceleration);
            metrics.Deceleration = Mathf.Max(0f, properties.Deceleration);
            metrics.TimeToTopSpeed = metrics.Acceleration > 0f ? metrics.MoveSpeed / metrics.Acceleration : 0f;
            metrics.DistanceToTopSpeed = 0.5f * metrics.MoveSpeed * metrics.TimeToTopSpeed;
            metrics.StopDistance = metrics.Deceleration > 0f ? metrics.MoveSpeed * metrics.MoveSpeed / (2f * metrics.Deceleration) : 0f;

            // Jump.
            metrics.JumpHeight = Mathf.Max(0f, properties.JumpHeight - properties.RideHeight);
            metrics.RiseSpeed = Mathf.Max(0.01f, properties.JumpSpeed);
            metrics.RiseTime = metrics.JumpHeight / metrics.RiseSpeed;
            metrics.HangTime = Mathf.Max(0f, properties.HangTimeDuration);
            metrics.FallAcceleration = gravity + Mathf.Max(0f, properties.FallSpeed) / dt;
            metrics.MaxFallSpeed = 0f;
            float fallTime = Mathf.Sqrt(2f * metrics.JumpHeight / Mathf.Max(0.01f, metrics.FallAcceleration));
            metrics.AirTime = metrics.RiseTime + metrics.HangTime + fallTime;
            metrics.JumpDistance = metrics.MoveSpeed * metrics.AirTime;
            metrics.JumpCooldown = Mathf.Max(0f, properties.JumpResetDelay);

            // Hover, if the character has it.
            var hover = ReadVariable<HoverControllerProperties>(prefab, typeof(HoverController), "Properties");
            if (hover != null)
            {
                metrics.CanHover = true;
                metrics.HoverTime = Mathf.Max(0f, hover.HoverTime);
                metrics.HoverSpeed = Mathf.Max(0f, hover.HorizontalSpeed);
                metrics.HoverFallSpeed = Mathf.Max(0f, hover.HoverFallSpeed);
                metrics.HoverDistance = metrics.HoverSpeed * metrics.HoverTime;
            }

            // Body size, for headroom: the character's sphere (the Motion Context uses one).
            var sphere = prefab.GetComponentInChildren<Box3DSphereShape>(true);
            if (sphere != null)
                metrics.BodyHeight = sphere.SphereRadius * 2f * Mathf.Abs(sphere.transform.lossyScale.y);

            return true;
        }

        /// <summary>The gravity of the Box3D world in the open scenes (positive, m/s²), or Box3D's default.</summary>
        public static float WorldGravity()
        {
            var world = UnityEngine.Object.FindFirstObjectByType<Box3DWorld>(FindObjectsInactive.Include);
            float g = world != null ? world.GravityVector.magnitude : 9.81f;
            return g > 0.01f ? g : 9.81f;
        }

        /// <summary>Every prefab in the project with the toolkit's character controller.</summary>
        public static List<GameObject> FindCharacterPrefabs()
        {
            var result = new List<GameObject>();
            var guids = AssetDatabase.FindAssets("t:Prefab");
            for (int i = 0; i < guids.Length; i++)
            {
                if (guids.Length > 200 && i % 50 == 0 &&
                    EditorUtility.DisplayCancelableProgressBar("Level Designer", "Looking for characters…", (float)i / guids.Length))
                    break;
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guids[i]));
                if (IsCharacter(prefab))
                    result.Add(prefab);
            }
            EditorUtility.ClearProgressBar();
            result.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase));
            return result;
        }

        // ---------------------------------------------------------------- reading Behavior Variables from prefabs

        private static BehaviorAttachment FindAttachment(GameObject root, Type behaviorType)
        {
            foreach (var schematic in root.GetComponentsInChildren<SchematicObject>(true))
            {
                if (schematic.Behaviors == null)
                    continue;
                foreach (var pair in schematic.Behaviors)
                {
                    if (pair.Value?.Items == null)
                        continue;
                    foreach (var attachment in pair.Value.Items)
                    {
                        if (attachment?.Metadata != null && attachment.Metadata.BehaviorType.Type == behaviorType)
                            return attachment;
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// A Behavior Variable's starting value on a prefab: the object's own override if it has one, otherwise the
        /// Behavior's default from its Metadata.
        /// </summary>
        public static T ReadVariable<T>(GameObject root, Type behaviorType, string variable) where T : UnityEngine.Object
        {
            var attachment = FindAttachment(root, behaviorType);
            if (attachment == null)
                return null;
            var value = attachment.FindOverride(variable)?.Value?.Get<T>();
            if (value == null)
            {
                var definition = attachment.Metadata.FindVariable(variable);
                if (definition?.Default != null)
                    value = definition.Default.Get<T>();
            }
            return value;
        }

        // ---------------------------------------------------------------- export

        [Serializable]
        private class ExportFile
        {
            public string Generated;
            public string Notes = "Heights in meters at the feet (0 = take-off ground). Speeds in m/s, times in seconds.";
            public List<CharacterMetrics> Characters = new();
        }

        /// <summary>The metrics as JSON (for tools and animators' scripts).</summary>
        public static string ToJson(IEnumerable<CharacterMetrics> metrics)
        {
            var file = new ExportFile { Generated = DateTime.Now.ToString("yyyy-MM-dd HH:mm") };
            file.Characters.AddRange(metrics);
            return JsonUtility.ToJson(file, true);
        }

        /// <summary>The metrics as CSV (one row per character, for spreadsheets).</summary>
        public static string ToCsv(IEnumerable<CharacterMetrics> metrics)
        {
            var builder = new StringBuilder();
            builder.AppendLine(CharacterMetrics.CsvHeader);
            foreach (var entry in metrics)
                builder.AppendLine(entry.ToCsvRow());
            return builder.ToString();
        }

        /// <summary>Asks where to save, then writes the metrics as JSON or CSV.</summary>
        public static void Export(IReadOnlyCollection<CharacterMetrics> metrics, bool csv)
        {
            if (metrics.Count == 0)
            {
                EditorUtility.DisplayDialog("Level Designer", "There are no character metrics to export.", "OK");
                return;
            }
            string extension = csv ? "csv" : "json";
            string path = EditorUtility.SaveFilePanel("Export Character Metrics", Application.dataPath, "CharacterMetrics." + extension, extension);
            if (string.IsNullOrEmpty(path))
                return;
            File.WriteAllText(path, csv ? ToCsv(metrics) : ToJson(metrics));
            if (path.StartsWith(Application.dataPath, StringComparison.OrdinalIgnoreCase))
                AssetDatabase.Refresh();
            EditorUtility.RevealInFinder(path);
        }
    }

    /// <summary>
    /// The character the blockout's Jump Arc and Run rulers are made from, picked in the Level Designer window's
    /// Characters tab (remembered per user).
    /// </summary>
    public static class ActiveCharacter
    {
        private const string Key = "Remedy.LevelDesigner.ActiveCharacter";
        private static GameObject _prefab;
        private static bool _loaded;

        /// <summary>Raised when another character is picked.</summary>
        public static event Action Changed;

        /// <summary>The picked character prefab, or null.</summary>
        public static GameObject Prefab
        {
            get
            {
                if (!_loaded)
                {
                    _loaded = true;
                    string guid = EditorPrefs.GetString(Key, "");
                    if (!string.IsNullOrEmpty(guid))
                        _prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                }
                return _prefab;
            }
            set
            {
                _loaded = true;
                _prefab = value;
                string path = value != null ? AssetDatabase.GetAssetPath(value) : null;
                EditorPrefs.SetString(Key, string.IsNullOrEmpty(path) ? "" : AssetDatabase.AssetPathToGUID(path));
                Changed?.Invoke();
                SceneView.RepaintAll();
            }
        }

        /// <summary>The picked character's metrics; false (and default numbers) when none is picked or it can't be read.</summary>
        public static bool TryGetMetrics(out CharacterMetrics metrics)
        {
            metrics = default;
            return Prefab != null && CharacterMetricsAnalyzer.TryAnalyze(Prefab, out metrics);
        }
    }
}
