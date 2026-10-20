using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Remedy.LevelDesigner.Editor
{
    /// <summary>
    /// The blockout toolbar's Character panel: the reference character (a Schematic prefab with the character
    /// controller) that the Jump Arc and Run rulers are measured from, its movement numbers, rulers placed from it, and
    /// exporting its numbers. Opens under the toolbar's Character button.
    /// </summary>
    public class CharacterPickerWindow : EditorWindow
    {
        private static readonly Vector2 Size = new(420f, 330f);
        private VisualElement _details;

        public static void Show(Rect screenRect)
        {
            foreach (var open in Resources.FindObjectsOfTypeAll<CharacterPickerWindow>())
                open.Close();
            var window = CreateInstance<CharacterPickerWindow>();
            window.titleContent = new GUIContent("Reference Character");
            window.ShowAsDropDown(screenRect, Size);
        }

        private void OnEnable() => ActiveCharacter.Changed += Refresh;
        private void OnDisable() => ActiveCharacter.Changed -= Refresh;

        private void CreateGUI()
        {
            var root = rootVisualElement;
            root.style.paddingLeft = root.style.paddingRight = root.style.paddingTop = root.style.paddingBottom = 6;

            var title = new Label("Reference Character") { style = { unityFontStyleAndWeight = FontStyle.Bold, marginBottom = 4 } };
            title.tooltip = "The character the blockout's Jump Arc and Run rulers are measured from.";
            root.Add(title);

            var row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
            var field = new ObjectField
            {
                objectType = typeof(GameObject),
                allowSceneObjects = false,
                value = ActiveCharacter.Prefab,
                tooltip = "A Schematic prefab with the Physics Based Character Controller",
                style = { flexGrow = 1 },
            };
            field.RegisterValueChangedCallback(evt =>
            {
                var prefab = evt.newValue as GameObject;
                if (prefab != null && !CharacterMetricsAnalyzer.IsCharacter(prefab))
                {
                    EditorUtility.DisplayDialog("Level Designer", $"{prefab.name} isn't a Schematic with the Physics Based Character Controller.", "OK");
                    field.SetValueWithoutNotify(evt.previousValue);
                    return;
                }
                ActiveCharacter.Prefab = prefab;
            });
            row.Add(field);
            var find = new Button { text = "Find ▾", tooltip = "Pick from the character prefabs in the project" };
            find.clicked += () =>
            {
                var menu = new GenericMenu();
                var found = CharacterMetricsAnalyzer.FindCharacterPrefabs();
                if (found.Count == 0)
                    menu.AddDisabledItem(new GUIContent("No character prefabs in the project"));
                foreach (var prefab in found)
                {
                    var pick = prefab;
                    menu.AddItem(new GUIContent(pick.name), pick == ActiveCharacter.Prefab, () => ActiveCharacter.Prefab = pick);
                }
                menu.DropDown(find.worldBound);
            };
            row.Add(find);
            root.Add(row);

            _details = new ScrollView { style = { flexGrow = 1, marginTop = 6 } };
            root.Add(_details);
            Refresh();
        }

        private void Refresh()
        {
            if (_details == null)
                return;
            rootVisualElement.Q<ObjectField>()?.SetValueWithoutNotify(ActiveCharacter.Prefab);
            _details.Clear();
            var prefab = ActiveCharacter.Prefab;
            if (prefab == null)
            {
                _details.Add(Note("Pick a character prefab (or drop one on the toolbar's Character button) to measure jumps and runs."));
                return;
            }
            if (!CharacterMetricsAnalyzer.TryAnalyze(prefab, out var metrics))
            {
                _details.Add(Note("The controller has no Properties assigned, so there's nothing to measure."));
                return;
            }

            var buttons = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap, marginBottom = 4 } };
            buttons.Add(new Button(() => PlaceRulerAtPivot(prefab, BlockoutRulerKind.JumpArc)) { text = "Jump Ruler Here", tooltip = "A jump arc ruler at the Scene view's pivot" });
            buttons.Add(new Button(() => PlaceRulerAtPivot(prefab, BlockoutRulerKind.RunDistance)) { text = "Run Ruler Here", tooltip = "A 1 second run ruler at the Scene view's pivot" });
            buttons.Add(new Button(() => CharacterMetricsAnalyzer.Export(new List<CharacterMetrics> { metrics }, false)) { text = "Export JSON" });
            buttons.Add(new Button(() => CharacterMetricsAnalyzer.Export(new List<CharacterMetrics> { metrics }, true)) { text = "Export CSV" });
            buttons.Add(new Button(() => EditorGUIUtility.PingObject(prefab)) { text = "Ping" });
            _details.Add(buttons);
            _details.Add(MetricsTable(metrics));
        }

        /// <summary>A character's movement numbers as a two-column table.</summary>
        public static VisualElement MetricsTable(CharacterMetrics m)
        {
            var box = new VisualElement();
            var table = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap } };
            void Row(string label, string value, string tooltip)
            {
                var cell = new VisualElement { tooltip = tooltip, style = { width = 195, flexDirection = FlexDirection.Row, marginBottom = 2 } };
                cell.Add(new Label(label) { style = { width = 105, color = new Color(0.7f, 0.7f, 0.7f) } });
                cell.Add(new Label(value) { style = { unityFontStyleAndWeight = FontStyle.Bold } });
                table.Add(cell);
            }
            string M(float v) => BlockoutShape.FormatMeters(v) + " m";
            string S(float v) => v.ToString("0.##") + " s";
            Row("Run speed", BlockoutShape.FormatMeters(m.MoveSpeed) + " m/s", "Top speed on the ground and in the air");
            Row("Run 1 s / 3 s", M(m.RunDistance(1f)) + " / " + M(m.RunDistance(3f)), "Distance from a standstill");
            Row("To top speed", S(m.TimeToTopSpeed) + ", " + M(m.DistanceToTopSpeed), "Time and distance to reach top speed");
            Row("Stopping", M(m.StopDistance), "Slide after letting go at top speed");
            Row("Jump height", M(m.JumpHeight), "How far the feet rise (highest ledge that can be jumped onto)");
            Row("Rise / hang", S(m.RiseTime) + " / " + S(m.HangTime), "Time going up, time held at the top");
            Row("Air time", S(m.AirTime), "A jump landing at the height it started");
            Row("Jump distance", M(m.JumpDistance), "A running jump landing at the starting height (widest gap)");
            Row("Fall", BlockoutShape.FormatMeters(m.FallAcceleration) + " m/s²", "Downward acceleration while falling, gravity included");
            Row("Jump again after", S(m.JumpCooldown), "Delay after landing before the next jump");
            if (m.CanHover)
            {
                Row("Hover", S(m.HoverTime) + " at " + BlockoutShape.FormatMeters(m.HoverSpeed) + " m/s", "A full hover");
                Row("Hover distance", M(m.HoverDistance), "Extra distance a full hover adds");
            }
            box.Add(table);
            box.Add(new Label($"Gravity {BlockoutShape.FormatMeters(m.Gravity)} m/s², physics step {m.FixedTimeStep:0.###} s. Predictions from the tuning: springs and collisions make the real motion a little softer.")
            {
                style = { fontSize = 10, color = new Color(0.6f, 0.6f, 0.6f), whiteSpace = WhiteSpace.Normal, marginTop = 2 },
            });
            return box;
        }

        /// <summary>Places a jump arc or run ruler for a character at the Scene view's pivot (blockouts only).</summary>
        public static void PlaceRulerAtPivot(GameObject prefab, BlockoutRulerKind kind)
        {
            if (LevelDesignerContext.Mode != LevelDesignerMode.Blockout)
            {
                EditorUtility.DisplayDialog("Level Designer", "Open a blockout first: rulers live in blockout scenes.", "OK");
                return;
            }
            if (!CharacterMetricsAnalyzer.TryAnalyze(prefab, out var metrics))
                return;
            var view = SceneView.lastActiveSceneView;
            Vector3 at = view != null ? view.pivot : Vector3.zero;
            var forward = view != null ? view.rotation * Vector3.forward : Vector3.forward;
            forward.y = 0f;
            var rotation = forward.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(forward.normalized) : Quaternion.identity;
            var ruler = LevelDesignerSceneGUI.CreateRuler(kind, LevelDesignerSettings.SnapPoint(at), rotation,
                $"{metrics.CharacterName} {(kind == BlockoutRulerKind.JumpArc ? "Jump" : "Run")}");
            ruler.Character = prefab;
            ruler.Metrics = metrics;
            Selection.activeGameObject = ruler.gameObject;
        }

        private static Label Note(string text) => new(text)
        {
            style = { whiteSpace = WhiteSpace.Normal, color = new Color(0.65f, 0.65f, 0.65f), marginTop = 4 },
        };
    }
}
