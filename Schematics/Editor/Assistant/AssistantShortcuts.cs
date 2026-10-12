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
        }

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
            if (!AssistantSettings.PushToTalkWins)
                return;
            var manager = ShortcutManager.instance;
            var ours = manager.GetShortcutBinding(AssistantMenu.PushToTalkId).keyCombinationSequence.ToList();
            if (ours.Count == 0)
                return;
            var clashing = manager.GetAvailableShortcutIds()
                .Where(id => id != AssistantMenu.PushToTalkId && manager.GetShortcutBinding(id).keyCombinationSequence.SequenceEqual(ours))
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
                var keys = saved.Split(',').Select(part => part.Split(':')).Where(p => p.Length == 2)
                    .Select(p => new KeyCombination((KeyCode)int.Parse(p[0]), (ShortcutModifiers)int.Parse(p[1]))).ToList();
                try { manager.RebindShortcut(id, keys.Count > 0 ? new ShortcutBinding(keys) : ShortcutBinding.empty); }
                catch (System.Exception exception) { Debug.LogWarning($"[Assistant] Couldn't put back {id}: {exception.Message}"); }
            }
            SaveTaken(new Dictionary<string, string>());
            Debug.Log($"[Assistant] Put back the shortcuts push-to-talk had taken ({string.Join(", ", taken.Keys)}).");
        }

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
