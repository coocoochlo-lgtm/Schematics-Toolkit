using UnityEditor;
using UnityEngine;

namespace Remedy.Audio
{
    /// <summary>
    /// What the world's spaces are doing, live in Play Mode: the place at the listener (from its Sound Field), every
    /// room reverb and echo wall with a level meter and whether sounds' sends reach it in time, and an Exaggerate slider
    /// to make every spatial effect plainly audible while testing.
    /// </summary>
    public class SoundSpacesWindow : EditorWindow
    {
        private Vector2 _scroll;
        private double _nextRepaint;

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

        private void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            EditorGUILayout.LabelField("Testing", EditorStyles.boldLabel);
            SoundSpaces.Exaggerate = EditorGUILayout.Slider(new GUIContent("Exaggerate", "Multiplies every spatial effect (place reverb and slapback, room and echo sends) so you can clearly hear them. 1 is as designed. Resets when Play Mode ends."), SoundSpaces.Exaggerate, 0f, 6f);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Sound Fields", EditorStyles.boldLabel);
            if (SoundField.All.Count == 0)
                EditorGUILayout.HelpBox("No Sound Field is loaded: Spatial effects use their own settings everywhere. Add one (Add Component > Remedy > Audio > Sound Field) and Bake it.", MessageType.Warning);
            foreach (var field in SoundField.All)
            {
                if (field == null) continue;
                bool baked = field.Data != null && field.Data.Count > 0;
                EditorGUILayout.LabelField(field.name, baked ? $"{field.Data.Count:N0} cells, {field.Data.Anchors?.Count ?? 0} echo anchors" : "not baked");
            }

            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("Enter Play Mode to see rooms, echo walls and sends live.", MessageType.Info);
                EditorGUILayout.EndScrollView();
                return;
            }

            var spaces = SoundSpaces.Current;
            var listener = spaces != null ? spaces.ListenerPosition : (FindFirstObjectByType<AudioListener>()?.transform.position ?? Vector3.zero);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("At the listener", EditorStyles.boldLabel);
            if (SoundField.Sample(listener, out var place))
            {
                EditorGUILayout.LabelField("Reverb Time", $"{place.ReverbTime:0.00} s");
                EditorGUILayout.LabelField("Reverb amount (Wet)", $"{place.Wet:0.00}  (0 open air … 0.65 bare closed room)");
                EditorGUILayout.LabelField("Enclosure", $"{place.Enclosure:0.00}  (rooms from {SoundSpaces.RoomEnclosure:0.0})");
                EditorGUILayout.LabelField("Size / nearest wall", $"{place.Size:0.0} m / {place.NearestWall:0.0} m");
                EditorGUILayout.LabelField("Slapback", place.SlapbackWet > 0.01f ? $"{place.SlapbackDelay * 1000f:0} ms, amount {place.SlapbackWet:0.00}, feedback {place.SlapbackFeedback:0.00}" : "none (the nearest wall is under ~9 m away: it would only double the sound)");
                EditorGUILayout.LabelField("Absorption / Hardness / Wetness", $"{place.Absorption:0.00} / {place.Hardness:0.00} / {place.Wetness:0.00}");
            }
            else
                EditorGUILayout.HelpBox("The listener isn't inside a baked Sound Field (or is inside a wall).", MessageType.None);

            if (spaces == null)
            {
                EditorGUILayout.HelpBox("No spatial sound has played yet (a Sound Cue with a Reverb or Delay in Spatial mode, inside a baked Sound Field).", MessageType.Info);
                EditorGUILayout.EndScrollView();
                return;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Room reverbs", EditorStyles.boldLabel);
            int shown = 0;
            foreach (var bus in spaces.RoomBuses)
            {
                if (!(bus.Owner is AcousticRoom room)) continue;
                shown++;
                Bus($"Room {room.Id} ({room.Cells} cells, {room.Acoustics.ReverbTime:0.0} s)", bus, Vector3.Distance(listener, room.Center));
            }
            if (shown == 0)
                EditorGUILayout.LabelField("None ringing: no Spatial Reverb has played in a room yet.", EditorStyles.miniLabel);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Echo walls", EditorStyles.boldLabel);
            shown = 0;
            foreach (var bus in spaces.EchoBuses)
            {
                if (bus.Owner == null) continue;
                shown++;
                Bus("Wall", bus, Vector3.Distance(listener, bus.transform.position));
            }
            if (shown == 0)
                EditorGUILayout.LabelField("None: no baked echo anchors within reach of the listener.", EditorStyles.miniLabel);

            EditorGUILayout.EndScrollView();
        }

        // One bus: its level, distance, whether it's playing, and its sends (taken in time / too late / too early).
        private static void Bus(string label, SpaceBus bus, float distance)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(label, $"{distance:0} m away");
                var rect = EditorGUILayout.GetControlRect(false, 10f);
                EditorGUI.DrawRect(rect, new Color(0f, 0f, 0f, 0.3f));
                float level = Mathf.Clamp01(bus.Peak * 2f);
                EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width * level, rect.height), Color.Lerp(new Color(0.3f, 0.85f, 0.5f), new Color(1f, 0.4f, 0.75f), level));
                string state = !bus.Running ? "not playing yet"
                    : bus.Late > bus.Accepted ? "sends arrive too late (timing is off)"
                    : bus.Accepted == 0 ? "playing, nothing sent yet"
                    : "OK";
                EditorGUILayout.LabelField("State", state);
                EditorGUILayout.LabelField("Sends", $"{bus.Accepted:N0} in time, {bus.Late:N0} late, {bus.Early:N0} too early; last {bus.LeadSeconds * 1000f:0} ms ahead");
            }
        }
    }
}
