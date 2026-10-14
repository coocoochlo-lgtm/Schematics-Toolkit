using System;
using System.Collections.Generic;
using UnityEngine;

namespace Remedy.Schematics.Collaboration
{
    /// <summary>
    /// Every collaboration problem goes to the Console through here, prefixed "[Collab]", so nothing fails silently. It
    /// also keeps the Console readable: the same message again within 30 seconds is counted instead of printed, and the
    /// next time it shows up after that it says how many times it happened meanwhile. Something that breaks every frame
    /// shows once every half minute, not sixty times a second. Safe to call from any thread.
    /// </summary>
    public static class CollabLog
    {
        public const string Prefix = "[Collab] ";
        private static readonly TimeSpan Window = TimeSpan.FromSeconds(30);

        private sealed class Seen
        {
            public DateTime First;
            public int Suppressed;
        }

        private static readonly Dictionary<string, Seen> _seen = new Dictionary<string, Seen>();

        /// <summary>
        /// Text appended to warnings and errors (the editor sets it to a link to the helper's log file,
        /// Library/SchematicsCollab/collab.log).
        /// </summary>
        public static string Footer;

        public static void Info(string message) => Debug.Log(Prefix + message);

        public static void Warning(string message)
        {
            if (Throttle("w|" + message, out int folded)) return;
            Debug.LogWarning(Prefix + message + Folded(folded) + FooterText());
        }

        public static void Error(string message)
        {
            if (Throttle("e|" + message, out int folded)) return;
            Debug.LogError(Prefix + message + Folded(folded) + FooterText());
        }

        /// <summary>An exception caught in a collaboration callback: logged with its stack trace, once per 30 s per kind.</summary>
        public static void Exception(string context, Exception e)
        {
            if (e == null) return;
            string message = string.IsNullOrEmpty(context) ? "Unexpected error" : context;
            if (Throttle("x|" + message + "|" + e.GetType().FullName + "|" + e.Message, out int folded)) return;
            Debug.LogError(Prefix + message + ": " + e.GetType().Name + ": " + e.Message + Folded(folded) + FooterText() + "\n" + e.StackTrace);
        }

        /// <summary>Runs an action, logging (never throwing) what goes wrong. For callbacks that must not break their caller.</summary>
        public static void Safe(string context, Action action)
        {
            if (action == null) return;
            try { action(); }
            catch (Exception e) { Exception(context, e); }
        }

        private static string Folded(int folded) => folded > 0 ? $" (and {folded} more times in the last 30 s)" : "";

        private static string FooterText() => string.IsNullOrEmpty(Footer) ? "" : "\n" + Footer;

        // True when this message was already shown within the window (it's counted instead).
        private static bool Throttle(string key, out int folded)
        {
            folded = 0;
            var now = DateTime.UtcNow;
            lock (_seen)
            {
                if (_seen.TryGetValue(key, out var seen) && now - seen.First < Window)
                {
                    seen.Suppressed++;
                    return true;
                }
                if (seen != null) folded = seen.Suppressed;
                if (_seen.Count > 1000) _seen.Clear();
                _seen[key] = new Seen { First = now };
                return false;
            }
        }
    }
}
