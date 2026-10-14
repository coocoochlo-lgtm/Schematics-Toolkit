using System;
using Remedy.Schematics.Collaboration;
using UnityEngine;

namespace Remedy.StickyNotes
{
    /// <summary>
    /// Notes over the runtime collaboration client (<see cref="CollabRuntime"/>), used by builds so playtesters' notes
    /// reach the editors in the same repo room. Everything is guarded: if the client throws (not set up, not connected)
    /// notes simply stay local.
    /// </summary>
    public sealed class CollabRuntimeTransport : IStickyNotesTransport
    {
        public bool IsConnected { get { try { return CollabRuntime.IsConnected; } catch { return false; } } }

        /// <summary>A build can't write to the repository; an editor in the room saves what it sends.</summary>
        public bool CanSaveToRepository => false;

        public string LocalPeerId { get { try { return CollabRuntime.LocalPeerId ?? ""; } catch { return ""; } } }

        public string LocalName
        {
            get
            {
                try { if (!string.IsNullOrEmpty(CollabRuntime.LocalName)) return CollabRuntime.LocalName; }
                catch { }
                return StickyNoteStore.FallbackName;
            }
        }

        public Color LocalColor { get { try { return CollabRuntime.LocalColor; } catch { return Color.white; } } }

        public event Action<string> SharedChanged;
        public event Action Connected;
        public event Action Disconnected;

        public CollabRuntimeTransport()
        {
            try
            {
                CollabRuntime.SharedChanged += key => SharedChanged?.Invoke(key);
                CollabRuntime.Connected += () => Connected?.Invoke();
                CollabRuntime.Disconnected += () => Disconnected?.Invoke();
            }
            catch (Exception e) { Debug.LogWarning($"[Sticky Notes] Runtime collaboration isn't available, notes stay local: {e.Message}"); }
        }

        public void Send(string channel, string json)
        {
            if (!IsConnected) return;
            try { CollabRuntime.Send(channel, json); }
            catch (Exception e) { Debug.LogException(e); }
        }

        public void Subscribe(string channel, Action<string, string> handler)
        {
            try { CollabRuntime.Subscribe(channel, handler); }
            catch (Exception e) { Debug.LogException(e); }
        }

        public void Unsubscribe(string channel, Action<string, string> handler)
        {
            try { CollabRuntime.Unsubscribe(channel, handler); }
            catch (Exception e) { Debug.LogException(e); }
        }

        public void SetShared(string key, string json)
        {
            if (!IsConnected) return;
            try { CollabRuntime.SetShared(key, json); }
            catch (Exception e) { Debug.LogException(e); }
        }

        public string GetShared(string key)
        {
            if (!IsConnected) return null;
            try { return CollabRuntime.GetShared(key); }
            catch { return null; }
        }
    }
}
