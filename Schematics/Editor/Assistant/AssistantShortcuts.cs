using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.ShortcutManagement;
using UnityEngine;

namespace Remedy.Schematics.Assistant
{
    /// <summary>
    /// Push-to-talk works in every window, so its key wins: any other shortcut bound to the same key (the View tool's
    /// Q, by default) is unbound, and remembered so it can be put back (Preferences > Schematics Assistant). The
    /// built-in Default shortcut profile can't be changed, so on it a "Schematics" profile is made and switched to.
    /// Changing push-to-talk's key in Edit > Shortcuts claims the new key the same way.
    /// <para>
    /// The Scene view's fly-mode keys (hold the right mouse button, then WASD/Q/E) are left alone: Q there is "fly
    /// down". Those shortcuts live in Unity's fly-mode context, which is only active while the right button is held,
    /// and the Shortcut Manager always prefers an active context's shortcut over a global one on the same key. So
    /// while flying, Q goes to the camera and push-to-talk never fires; the rest of the time Q is push-to-talk. (Older
    /// versions of this class unbound "Fly Mode Down" too; <see cref="Claim"/> gives it its key back.) As a second
    /// line of defence, push-to-talk also ignores the key while <see cref="SceneFlying"/> says the view is being flown.
    /// </para>
    /// </summary>
    [InitializeOnLoad]
    internal static class AssistantShortcuts
    {
        private const string Profile = "Schematics";
        private const string TakenKey = "Schematics.Assistant.TakenShortcuts";

        static AssistantShortcuts()
        {
            EditorApplication.delayCall += Claim;
            ShortcutManager.instance.shortcutBindingChanged += evt =>
            {
                if (evt.shortcutId == AssistantMenu.PushToTalkId)
                    EditorApplication.delayCall += Claim;
            };
            ShortcutManager.instance.activeProfileChanged += _ => EditorApplication.delayCall += Claim;
            SceneView.duringSceneGui -= TrackRightButton;
            SceneView.duringSceneGui += TrackRightButton;
        }

        #region Flying in the Scene view

        private static bool _rightButtonHeld;

        /// <summary>
        /// True while the Scene view camera is being moved by hand: the right mouse button is held in a Scene view
        /// (fly mode, where WASD/Q/E move the camera) or one of Unity's view tools is active (orbit, pan, zoom).
        /// Push-to-talk does nothing then, so Q stays "fly down".
        /// </summary>
        public static bool SceneFlying
            // Flying keeps the Scene view focused; if something else has focus, the release was missed somewhere.
            => (_rightButtonHeld && EditorWindow.focusedWindow is SceneView) || Tools.viewToolActive;

        // Every Scene view event: remember whether the right button is down. rawType, because the camera controls
        // have usually used the event by the time this sees it.
        private static void TrackRightButton(SceneView view)
        {
            var current = Event.current;
            if (current == null || current.button != 1)
            {
                // Lost the button some other way (focus moved, a window closed under it): no view tool, no flying.
                if (_rightButtonHeld && current != null && current.rawType == EventType.MouseMove && !Tools.viewToolActive)
                    _rightButtonHeld = false;
                return;
            }
            switch (current.rawType)
            {
                case EventType.MouseDown:
                    _rightButtonHeld = true;
                    break;
                case EventType.MouseUp:
                    _rightButtonHeld = false;
                    break;
            }
        }

        /// <summary>The Scene view's fly-mode keys (Unity's "3D Viewport/Fly Mode Forward/Back/Left/Right/Up/Down").</summary>
        private static bool IsFlyMode(string id) => id != null && id.IndexOf("Fly Mode", System.StringComparison.OrdinalIgnoreCase) >= 0;

        #endregion

        /// <summary>The push-to-talk key, for showing ("Q", "Shift+T"...), or "none".</summary>
        public static string KeyName
        {
            get
            {
                var keys = ShortcutManager.instance.GetShortcutBinding(AssistantMenu.PushToTalkId).keyCombinationSequence.ToList();
                return keys.Count == 0 ? "none" : string.Join(", ", keys.Select(k => k.ToString()));
            }
        }

        /// <summary>Unbinds the other shortcuts on push-to-talk's key.</summary>
        public static void Claim()
        {
            GiveBackFlyMode();
            if (!AssistantSettings.PushToTalkWins)
                return;
            var manager = ShortcutManager.instance;
            var ours = manager.GetShortcutBinding(AssistantMenu.PushToTalkId).keyCombinationSequence.ToList();
            if (ours.Count == 0)
                return;
            var clashing = manager.GetAvailableShortcutIds()
                .Where(id => id != AssistantMenu.PushToTalkId && !IsFlyMode(id) && manager.GetShortcutBinding(id).keyCombinationSequence.SequenceEqual(ours))
                .ToList();
            if (clashing.Count == 0)
                return;

            try
            {
                if (manager.IsProfileReadOnly(manager.activeProfileId))
                {
                    if (!manager.GetAvailableProfileIds().Contains(Profile))
                        manager.CreateProfile(Profile);
                    manager.activeProfileId = Profile;
                    Debug.Log($"[Assistant] Switched to the \"{Profile}\" shortcut profile (the Default one can't be changed).");
                }
                var taken = Taken();
                foreach (var id in clashing)
                {
                    var keys = manager.GetShortcutBinding(id).keyCombinationSequence.ToList();
                    taken[id] = string.Join(",", keys.Select(k => $"{(int)k.keyCode}:{(int)k.modifiers}"));
                    manager.RebindShortcut(id, ShortcutBinding.empty);
                }
                SaveTaken(taken);
                Debug.Log($"[Assistant] {KeyName} is Sprocket's push-to-talk key everywhere now; it was also {string.Join(", ", clashing)}, "
                    + "which no longer has a key. Put it back in Preferences > Schematics Assistant (or pick another key in Edit > Shortcuts).");
            }
            catch (System.Exception exception)
            {
                Debug.LogWarning($"[Assistant] Couldn't free {KeyName} for push-to-talk: {exception.Message}");
            }
        }

        /// <summary>Gives the shortcuts push-to-talk took their keys back.</summary>
        public static void Restore()
        {
            var manager = ShortcutManager.instance;
            var taken = Taken();
            if (taken.Count == 0 || manager.IsProfileReadOnly(manager.activeProfileId))
                return;
            foreach (var (id, saved) in taken)
            {
                var keys = Parse(saved);
                // A binding holds one key combination (on this Unity version, the constructor takes just one).
                try { manager.RebindShortcut(id, keys.Count > 0 ? new ShortcutBinding(keys[0]) : ShortcutBinding.empty); }
                catch (System.Exception exception) { Debug.LogWarning($"[Assistant] Couldn't put back {id}: {exception.Message}"); }
            }
            SaveTaken(new Dictionary<string, string>());
            Debug.Log($"[Assistant] Put back the shortcuts push-to-talk had taken ({string.Join(", ", taken.Keys)}).");
        }

        // The fly-mode keys are only live while flying (and win over push-to-talk then), so they were never in the way:
        // any an earlier version took get their key back.
        private static void GiveBackFlyMode()
        {
            var manager = ShortcutManager.instance;
            var taken = Taken();
            var fly = taken.Keys.Where(IsFlyMode).ToList();
            if (fly.Count == 0 || manager.IsProfileReadOnly(manager.activeProfileId))
                return;
            foreach (var id in fly)
            {
                var keys = Parse(taken[id]);
                try
                {
                    // Only if nobody has given it a key since.
                    if (!manager.GetShortcutBinding(id).keyCombinationSequence.Any())
                        manager.RebindShortcut(id, keys.Count > 0 ? new ShortcutBinding(keys[0]) : ShortcutBinding.empty);
                    taken.Remove(id);
                }
                catch (System.Exception exception) { Debug.LogWarning($"[Assistant] Couldn't put back {id}: {exception.Message}"); }
            }
            SaveTaken(taken);
            Debug.Log($"[Assistant] Gave the Scene view's fly-mode keys back ({string.Join(", ", fly)}): Q flies down while the right mouse button is held, and is push-to-talk the rest of the time.");
        }

        private static List<KeyCombination> Parse(string saved)
            => saved.Split(',').Select(part => part.Split(':')).Where(p => p.Length == 2)
                .Select(p => new KeyCombination((KeyCode)int.Parse(p[0]), (ShortcutModifiers)int.Parse(p[1]))).ToList();

        public static bool AnyTaken => Taken().Count > 0;

        private static Dictionary<string, string> Taken()
        {
            var result = new Dictionary<string, string>();
            foreach (var entry in EditorPrefs.GetString(TakenKey, "").Split('\n'))
            {
                int bar = entry.IndexOf('|');
                if (bar > 0) result[entry.Substring(0, bar)] = entry.Substring(bar + 1);
            }
            return result;
        }

        private static void SaveTaken(Dictionary<string, string> taken)
            => EditorPrefs.SetString(TakenKey, string.Join("\n", taken.Select(pair => pair.Key + "|" + pair.Value)));
    }
}
