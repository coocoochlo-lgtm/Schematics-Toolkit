using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Remedy.Audio
{
    /// <summary>
    /// What the world's spaces are doing, live in Play Mode: the place at the listener and what a Spatial sound there
    /// gets (and why, when it's dry), every ringing room and echo wall with a level meter and whether sounds reach it in
    /// time, and an Exaggerate slider to make every spatial effect plainly audible while testing.
    /// </summary>
    public class SoundSpacesWindow : EditorWindow
    {
        private Vector2 _scroll;
        private double _nextRepaint;
        private static GUIStyle _verdict, _row, _dim;

        [MenuItem("Tools/Remedy/Audio/Sound Spaces Debug")]
        public static void Open() => GetWindow<SoundSpacesWindow>("Sound Spaces");

        private void OnEnable() => EditorApplication.update += Tick;
        private void OnDisable() => EditorApplication.update -= Tick;

        private void Tick()
        {
            if (EditorApplication.timeSinceStartup < _nextRepaint) return;
            _nextRepaint = EditorApplication.timeSinceStartup + 0.1;
            Repaint();
        }

        private static void Styles()
        {
            _verdict ??= new GUIStyle(EditorStyles.wordWrappedLabel) { fontSize = 13, fontStyle = FontStyle.Bold, margin = new RectOffset(4, 4, 6, 6) };
            _row ??= new GUIStyle(EditorStyles.wordWrappedLabel) { fontSize = 12 };
            _dim ??= new GUIStyle(EditorStyles.wordWrappedMiniLabel) { fontSize = 11 };
        }

        private void OnGUI()
        {
            Styles();
            float labelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = Mathf.Clamp(position.width * 0.4f, 120f, 220f);
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            Section("Testing");
            SoundSpaces.Exaggerate = EditorGUILayout.Slider(new GUIContent("Exaggerate", "Multiplies every spatial effect so you can clearly hear whether it works. 1 is as designed; resets when Play Mode ends."), SoundSpaces.Exaggerate, 0f, 6f);

            SoundField.ShowEchoWalls = EditorGUILayout.Toggle(new GUIContent("Show Echo Walls", "Draw every baked echo wall in the Scene view, glowing with how much it's echoing (in Play Mode)."), SoundField.ShowEchoWalls);
            if (GUI.changed)
                SceneView.RepaintAll();

            Section("Sound Fields");
            if (SoundField.All.Count == 0)
                Verdict("No Sound Field is loaded, so Spatial effects act as open air (nearly dry) everywhere.");
            foreach (var field in SoundField.All)
            {
                if (field == null) continue;
                bool baked = field.Data != null && field.Data.Count > 0;
                Row(field.name, baked ? $"{field.Data.Count:N0} cells · {field.Data.Anchors?.Count ?? 0} echo walls{(field.Data.HasCellWalls ? "" : " (old bake: bake again for walls)")}" : "NOT BAKED");
            }

            if (!Application.isPlaying)
            {
                Verdict("Enter Play Mode to see what sounds get, live.");
                End(labelWidth);
                return;
            }

            var spaces = SoundSpaces.Current;
            Voices(spaces);
            var listenerObject = FindFirstObjectByType<AudioListener>();
            var listener = listenerObject != null ? listenerObject.transform.position : Vector3.zero;

            Section("Where you are");
            if (!SoundField.Sample(listener, out var place))
                Verdict("Outside every baked Sound Field: Spatial effects here act as open air (nearly dry, no slapback).");
            else
            {
                Verdict(Summary(place, spaces, listener));
                Row("Reverb", $"{place.ReverbTime:0.0} s long, amount {place.Wet:0.00}");
                Row("Slapback (Delay)", place.SlapbackWet > 0.01f ? $"{place.SlapbackDelay * 1000f:0} ms, amount {place.SlapbackWet:0.00}" : "none");
                Row("Closed in", $"{place.Enclosure * 100f:0}%  (a room from {SoundSpaces.RoomEnclosure * 100f:0}%)");
                Row("Space size", $"{place.Size:0.0} m across · nearest surface {place.NearestWall:0.0} m");
                Row("Surfaces", $"absorb {place.Absorption * 100f:0}% · hard {place.Hardness * 100f:0}% · wet {place.Wetness * 100f:0}%");
                Row("Echo surfaces in reach", $"{EchoSurfacesInReach(listener)} (within {SoundSpaces.EchoRange:0} m)");
            }

            Section("Ringing rooms");
            int shown = 0;
            if (spaces != null)
                foreach (var bus in spaces.RoomBuses)
                {
                    if (!(bus.Owner is AcousticRoom room)) continue;
                    shown++;
                    Bus($"Room {room.Id}", $"{room.Acoustics.ReverbTime:0.0} s · {Vector3.Distance(listener, room.Center):0} m", bus);
                }
            if (shown == 0)
                Dim("None: no Spatial Reverb has played inside a room yet.");

            Section("Echo walls");
            shown = 0;
            if (spaces != null)
                foreach (var bus in spaces.EchoBuses)
                {
                    if (bus.Owner == null) continue;
                    shown++;
                    Bus("Wall", $"{Vector3.Distance(listener, bus.transform.position):0} m away", bus);
                }
            if (shown == 0)
                Dim("None: no sound has been heard off an echo surface yet (or none is in reach).");

            End(labelWidth);
        }

        // How many of Unity's real voices the spaces hold: past its limit, sounds start going silent.
        private static void Voices(SoundSpaces spaces)
        {
            Section("Voices");
            int playing = 0;
            if (spaces != null)
            {
                foreach (var bus in spaces.RoomBuses) if (bus != null && bus.Source.isPlaying) playing++;
                foreach (var bus in spaces.EchoBuses) if (bus != null && bus.Source.isPlaying) playing++;
                foreach (var bus in spaces.Tails.Buses) if (bus != null && bus.Source.isPlaying) playing++;
                foreach (var bus in spaces.Corners.Buses) if (bus != null && bus.Source.isPlaying) playing++;
            }
            int real = AudioSettings.GetConfiguration().numRealVoices;
            Row("Rooms, walls, spots and corners playing", $"{playing} of Unity's {real} real voices");
            if (playing * 2 > real)
                Verdict($"Spatial effects are using over half of Unity's voices: other sounds may be cut. Raise Max Real Voices (Project Settings > Audio) to 64 or more.");
        }

        // Why a Spatial sound where the listener is sounds the way it does, in a sentence.
        private static string Summary(in SoundAcoustics place, SoundSpaces spaces, Vector3 listener)
        {
            var reasons = new List<string>();
            if (place.Enclosure >= SoundSpaces.RoomEnclosure)
                reasons.Add("you're in a room: Spatial Reverb rings the room");
            else if (place.Wet < 0.05f)
                reasons.Add("open air: almost no reverb (the sky soaks it up)");
            if (place.SlapbackWet < 0.01f)
                reasons.Add(place.NearestWall < 8.6f
                    ? $"no slapback (the nearest surface, {place.NearestWall:0.0} m, is too close to be heard as an echo)"
                    : "no slapback (the surfaces are soft or absorbent)");
            if (EchoSurfacesInReach(listener) == 0)
                reasons.Add("no echo walls in reach (none were found near here when baking; bake again after changing the level or its materials)");
            return reasons.Count == 0 ? "Reverb, slapback and echoes are all available here." : char.ToUpper(reasons[0][0]) + string.Join("; ", reasons).Substring(1) + ".";
        }

        private static int EchoSurfacesInReach(Vector3 listener)
        {
            int count = 0;
            foreach (var field in SoundField.All)
            {
                if (field?.Data?.Anchors == null) continue;
                foreach (var anchor in field.Data.Anchors)
                    if (Vector3.Distance(listener, field.transform.TransformPoint(anchor.Position)) <= SoundSpaces.EchoRange)
                        count++;
            }
            return count;
        }

        // One room or wall on one line: its meter, where, and in plain words how it's doing.
        private static void Bus(string name, string where, SpaceBus bus)
        {
            var rect = EditorGUILayout.GetControlRect(false, 20f);
            var meter = new Rect(rect.x, rect.y + 4f, 70f, 12f);
            EditorGUI.DrawRect(meter, new Color(0f, 0f, 0f, 0.35f));
            float level = Mathf.Clamp01(bus.Peak * 2f);
            EditorGUI.DrawRect(new Rect(meter.x, meter.y, meter.width * level, meter.height), Color.Lerp(new Color(0.3f, 0.85f, 0.5f), new Color(1f, 0.4f, 0.75f), level));
            string state = !bus.Running ? "starting"
                : bus.Late > bus.Accepted ? "SOUNDS ARRIVE TOO LATE"
                : bus.Accepted == 0 ? "waiting for sounds"
                : $"OK ({bus.Accepted:N0} in time, {bus.Late:N0} late)";
            if (bus.Resets > 0)
                state += $" · ran away {bus.Resets:N0}x (cleared)";
            GUI.Label(new Rect(rect.x + 78f, rect.y, rect.width - 78f, rect.height), $"{name} · {where} · {state}", _row);
        }

        private static void Section(string title)
        {
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            var rect = EditorGUILayout.GetControlRect(false, 1f);
            EditorGUI.DrawRect(rect, new Color(0.5f, 0.5f, 0.5f, 0.4f));
        }

        private static void Verdict(string text) => EditorGUILayout.LabelField(text, _verdict);

        private static void Row(string label, string value)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(label, GUILayout.Width(EditorGUIUtility.labelWidth));
                EditorGUILayout.LabelField(value, _row);
            }
        }

        private static void Dim(string text) => EditorGUILayout.LabelField(text, _dim);

        private void End(float labelWidth)
        {
            EditorGUILayout.EndScrollView();
            EditorGUIUtility.labelWidth = labelWidth;
        }
    }

    /// <summary>
    /// Keeps the Scene view redrawing about 15 times a second in Play Mode while echo walls are shown, so their glow
    /// follows the sound (the Scene view otherwise only redraws when something in it is touched).
    /// </summary>
    [InitializeOnLoad]
    internal static class EchoWallRepaint
    {
        private static double _next;

        static EchoWallRepaint() => EditorApplication.update += Tick;

        private static void Tick()
        {
            if (!EditorApplication.isPlaying || !SoundField.ShowEchoWalls || SoundField.All.Count == 0)
                return;
            if (EditorApplication.timeSinceStartup < _next)
                return;
            _next = EditorApplication.timeSinceStartup + 1.0 / 15.0;
            SceneView.RepaintAll();
        }
    }
}
