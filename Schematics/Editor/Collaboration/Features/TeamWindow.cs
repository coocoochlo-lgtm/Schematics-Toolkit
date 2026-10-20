using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Remedy.Schematics.Collaboration
{
    /// <summary>
    /// Window/Schematics/Team: who's here and what they're working on, plus chat.
    /// Each teammate shows a circle in their colour with their initials, name, a crown for the host, ping, what they have
    /// open and whether they're active. Double-click someone to jump to them: same scene or graph, same view.
    /// </summary>
    public sealed class TeamWindow : EditorWindow
    {
        private Vector2 _peerScroll, _chatScroll;
        private string _draft = "";
        private int _unread;
        private bool _scrollChatToEnd = true;
        private bool _refocus;
        private string _selectedPeer;
        private GUIStyle _nameStyle, _detailStyle, _chatStyle, _badgeStyle;

        [MenuItem("Window/Schematics/Team")]
        public static void Open()
        {
            var window = GetWindow<TeamWindow>();
            window.UpdateTitle();
            window.minSize = new Vector2(260, 320);
            window.Show();
        }

        private void OnEnable()
        {
            CollabSession.PeerJoined += OnPeerEvent;
            CollabSession.PeerLeft += OnPeerEvent;
            CollabSession.PeerChanged += OnPeerEvent;
            CollabSession.Connected += Repaint;
            CollabSession.Disconnected += Repaint;
            CollabSession.HostChanged += Repaint;
            ChatService.MessageAdded += OnChat;
            UpdateTitle();
        }

        private void OnDisable()
        {
            CollabSession.PeerJoined -= OnPeerEvent;
            CollabSession.PeerLeft -= OnPeerEvent;
            CollabSession.PeerChanged -= OnPeerEvent;
            CollabSession.Connected -= Repaint;
            CollabSession.Disconnected -= Repaint;
            CollabSession.HostChanged -= Repaint;
            ChatService.MessageAdded -= OnChat;
        }

        private void OnFocus()
        {
            _unread = 0;
            UpdateTitle();
        }

        private void OnPeerEvent(CollabPeer _) => Repaint();

        private void OnChat(ChatMsg msg)
        {
            _scrollChatToEnd = true;
            bool mine = msg.from == CollabFeatures.LocalId;
            if (!mine && focusedWindow != this)
            {
                _unread++;
                UpdateTitle();
                if (CollabFeaturesSettings.instance.ChatSound) EditorApplication.Beep();
            }
            Repaint();
        }

        private void UpdateTitle()
        {
            titleContent = new GUIContent(_unread > 0 ? $"Team ({_unread})" : "Team", titleContent?.image);
        }

        private void InitStyles()
        {
            if (_nameStyle != null) return;
            _nameStyle = new GUIStyle(EditorStyles.boldLabel) { richText = true };
            _detailStyle = new GUIStyle(EditorStyles.miniLabel) { richText = true };
            _chatStyle = new GUIStyle(EditorStyles.wordWrappedLabel) { richText = true };
            _badgeStyle = new GUIStyle(EditorStyles.miniBoldLabel) { alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
        }

        private void OnGUI()
        {
            InitStyles();
            DrawHeader();
            float chatHeight = Mathf.Max(140, position.height * 0.45f);
            DrawPeers(position.height - chatHeight - 24);
            DrawChat();
        }

        #region Header

        private void DrawHeader()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                bool connected = CollabSession.IsConnected;
                var dot = GUILayoutUtility.GetRect(12, 18, GUILayout.Width(12));
                DrawDisc(new Rect(dot.x + 1, dot.y + 4, 10, 10), connected ? new Color(0.3f, 0.85f, 0.4f) : new Color(0.6f, 0.6f, 0.6f));
                int count = VisiblePeers(out _).Count;
                string state = connected ? $"Connected · {count} {(count == 1 ? "person" : "people")}" : "Not connected · " + CollabStatus.Message;
                if (connected && EditorApplication.isPlayingOrWillChangePlaymode) state += " · paused (Play Mode)";
                int playtesters = (CollabSession.Peers ?? (IReadOnlyList<CollabPeer>)Array.Empty<CollabPeer>()).Count(p => !p.IsLocal && p.Role == CollabRole.Playtester);
                if (connected && playtesters > 0) state += $" · {playtesters} playtesting";
                GUILayout.Label(state, EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();
                if (CollabGit.HasRepository && !string.IsNullOrEmpty(CollabGit.Branch))
                    GUILayout.Label("⎇ " + CollabGit.Branch, EditorStyles.miniLabel);
                if (GUILayout.Button("⋮", EditorStyles.toolbarButton, GUILayout.Width(22))) ShowMenu();
            }
        }

        private void ShowMenu()
        {
            var menu = new GenericMenu();
            if (CollabSession.IsConnected && CollabSession.IsHost)
                menu.AddItem(new GUIContent("Commit Edited Scenes Now"), false, () => SceneSync.CommitAll(force: false, summary: "Collaborative edits"));
            else menu.AddDisabledItem(new GUIContent("Commit Edited Scenes Now (host only)"));
            if (CollabGit.HasRepository) menu.AddItem(new GUIContent("Pull Now"), false, () => CollabFeatures.Safe(CollabGit.PullNow));
            menu.AddItem(new GUIContent("Receive Live Edits In Play Mode"), CollabLiveEdits.Enabled, () => CollabLiveEdits.Enabled = !CollabLiveEdits.Enabled);
            menu.AddItem(new GUIContent("Replace Arrived Placeholders"), false, () => CollabPlaceholders.ResolveAll());
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Collaboration Features Settings…"), false, () => SettingsService.OpenProjectSettings("Project/Schematics/Collaboration Features"));
            menu.ShowAsContext();
        }

        #endregion

        #region Peers

        private void DrawPeers(float height)
        {
            var peers = VisiblePeers(out int hidden)
                .OrderByDescending(p => p.IsLocal).ThenByDescending(p => p.IsHost).ThenBy(p => p.Name).ToList();
            _peerScroll = EditorGUILayout.BeginScrollView(_peerScroll, GUILayout.Height(Mathf.Max(60, height)));
            if (!CollabSession.IsConnected)
                EditorGUILayout.HelpBox("Not in a session. Teammates on the same network or relay appear here once you're connected.", MessageType.Info);
            foreach (var peer in peers) DrawPeer(peer);
            if (hidden > 0)
                GUILayout.Label(new GUIContent($"<color=#888888>{hidden} leftover connection{(hidden == 1 ? "" : "s")} hidden</color>",
                    "Extra entries for the same person with no editor behind them (helpers left running by an older version of the " +
                    "collaboration tools on their machine). The host removes them within a couple of minutes; restarting that person's " +
                    "Unity also clears them."), _detailStyle);
            EditorGUILayout.EndScrollView();
        }

        private void DrawPeer(CollabPeer peer)
        {
            var rect = GUILayoutUtility.GetRect(10, 46, GUILayout.ExpandWidth(true));
            var e = Event.current;
            bool selected = _selectedPeer == peer.Id;
            if (e.type == EventType.Repaint)
            {
                if (selected) EditorGUI.DrawRect(rect, new Color(0.24f, 0.37f, 0.59f, 0.35f));
                else if (rect.Contains(e.mousePosition)) EditorGUI.DrawRect(rect, new Color(1, 1, 1, 0.04f));
            }

            var avatarRect = new Rect(rect.x + 6, rect.y + 5, 36, 36);
            DrawAvatar(avatarRect, peer);

            float x = avatarRect.xMax + 8;
            var hex = CollabFeatures.Hex(peer.Color);
            string name = $"<color={hex}>{Escape(peer.Name)}</color>";
            if (peer.IsLocal) name += " <color=#888888>(you)</color>";
            if (peer.IsHost) name += " <color=#E8B931>♛ host</color>";
            var nameContent = new GUIContent(name);
            float nameWidth = Mathf.Max(40f, Mathf.Min(_nameStyle.CalcSize(nameContent).x, rect.width - x - 150));
            GUI.Label(new Rect(x, rect.y + 4, nameWidth, 18), nameContent, _nameStyle);
            DrawRoleBadge(new Rect(x + nameWidth + 4, rect.y + 5, 90, 16), peer);

            if (!peer.IsLocal)
            {
                var ping = peer.PingMs;
                var pingColor = ping < 80 ? "#6BC46D" : ping < 200 ? "#E8B931" : "#E05A4F";
                var pingText = ping < 0 ? "<color=#888888>… ms</color>" : $"<color={pingColor}>{ping} ms</color>";
                GUI.Label(new Rect(rect.xMax - 64, rect.y + 4, 58, 18), pingText, new GUIStyle(_detailStyle) { alignment = TextAnchor.MiddleRight });
            }

            GUI.Label(new Rect(x, rect.y + 23, rect.width - x - 6, 16), Describe(peer), _detailStyle);

            if (e.type == EventType.MouseDown && e.button == 0 && rect.Contains(e.mousePosition))
            {
                _selectedPeer = peer.Id;
                if (e.clickCount == 2 && !peer.IsLocal) CollabNavigation.JumpTo(peer);
                e.Use();
                Repaint();
            }
            else if (e.type == EventType.ContextClick && rect.Contains(e.mousePosition) && !peer.IsLocal)
            {
                var menu = new GenericMenu();
                menu.AddItem(new GUIContent("Jump to " + peer.Name), false, () => CollabNavigation.JumpTo(peer));
                menu.ShowAsContext();
                e.Use();
            }
        }

        /// <summary>
        /// Everyone, with leftover duplicates of one person folded away: several entries with the same name, git email,
        /// role and kind (editor/build) where some have an editor behind them (they publish presence) and the others
        /// don't. Those others are helpers an older version left running; they look like the same person many times.
        /// </summary>
        internal static List<CollabPeer> VisiblePeers(out int hidden)
        {
            hidden = 0;
            var all = (CollabSession.Peers ?? (IReadOnlyList<CollabPeer>)Array.Empty<CollabPeer>()).Where(p => p != null).ToList();
            var result = new List<CollabPeer>();
            foreach (var group in all.GroupBy(p => (p.Name ?? "", Email(p), p.IsBuild, p.Role)))
            {
                var members = group.ToList();
                if (members.Count == 1) { result.Add(members[0]); continue; }
                var live = members.Where(p => p.IsLocal || HasEditorBehind(p)).ToList();
                if (live.Count == 0) live.Add(members.OrderByDescending(p => p.IsHost).First());
                result.AddRange(live);
                hidden += members.Count - live.Count;
            }
            return result;
        }

        private static string Email(CollabPeer peer) =>
            peer.Presence != null && peer.Presence.TryGetValue("git.email", out var email) ? email ?? "" : "";

        private static bool HasEditorBehind(CollabPeer peer) =>
            peer.Presence != null && (peer.Presence.ContainsKey(CollabChannels.PresenceLocation) || peer.Presence.ContainsKey(CollabChannels.PresenceCamera));

        /// <summary>"EDITOR" or "PLAYTESTER" (with "· BUILD" for a build of the game), as a small coloured pill.</summary>
        private void DrawRoleBadge(Rect rect, CollabPeer peer)
        {
            bool playing = peer.Role == CollabRole.Playtester;
            string text = playing ? (peer.IsBuild ? "PLAYTESTER · BUILD" : "PLAYTESTER") : "EDITOR";
            var style = new GUIStyle(_badgeStyle) { fontSize = 9, padding = new RectOffset(5, 5, 0, 0) };
            var size = style.CalcSize(new GUIContent(text));
            var badge = new Rect(rect.x, rect.y, size.x + 2, rect.height);
            var background = playing ? new Color(0.35f, 0.63f, 0.88f, 0.9f) : new Color(0.45f, 0.45f, 0.5f, 0.9f);
            if (Event.current.type == EventType.Repaint)
                GUI.DrawTexture(badge, EditorGUIUtility.whiteTexture, ScaleMode.StretchToFill, true, 0, background, 0, rect.height * 0.5f);
            GUI.Label(badge, new GUIContent(text, playing
                ? (peer.IsBuild ? "Playing a build of the game. Your scene edits reach it if they turned on Receive Live Edits." : "Playing in their editor's Play Mode.")
                : "Editing in a Unity editor."), style);
        }

        private static string Describe(CollabPeer peer)
        {
            var location = CollabFeatures.GetPresence<LocationPresence>(peer, CollabChannels.PresenceLocation);
            var control = CollabFeatures.GetPresence<CollabControlPresence>(peer, CollabRuntimeGhosts.ControlPresenceKey);
            string controls = control?.names != null && control.names.Count > 0
                ? $"  <color=#5AA0E0>▶ controls {Escape(string.Join(", ", control.names))}</color>" : "";
            if (location == null) return string.IsNullOrEmpty(controls) ? "<color=#888888>—</color>" : controls.TrimStart();
            string where = location.kind switch
            {
                "scene" => "Scene: " + Escape(CollabLevelDesignerLink.DisplayName(location.path)) + (location.scenes != null && location.scenes.Count > 1 ? $" (+{location.scenes.Count - 1})" : ""),
                "graph" => "Graph: " + Escape(location.name),
                _ => "Nothing open",
            };
            string statusColor = location.status == "Active" ? "#6BC46D" : location.status == "Play Mode" ? "#5AA0E0" : "#888888";
            return $"{where}  <color={statusColor}>● {Escape(location.status)}</color>{controls}";
        }

        /// <summary>A disc in their colour with their initials.</summary>
        private void DrawAvatar(Rect rect, CollabPeer peer)
        {
            DrawDisc(rect, peer.Color);
            var initials = string.Concat((peer.Name ?? "?").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Take(2).Select(w => char.ToUpperInvariant(w[0])));
            var style = new GUIStyle(_badgeStyle) { fontSize = 13, normal = { textColor = peer.Color.grayscale > 0.6f ? Color.black : Color.white } };
            GUI.Label(rect, initials, style);
        }

        private static void DrawDisc(Rect rect, Color color)
        {
            GUI.DrawTexture(rect, EditorGUIUtility.whiteTexture, ScaleMode.StretchToFill, true, 0, color, 0, rect.width * 0.5f);
        }

        #endregion

        #region Chat

        private void DrawChat()
        {
            EditorGUILayout.LabelField("Chat", EditorStyles.boldLabel);
            _chatScroll = EditorGUILayout.BeginScrollView(_chatScroll, EditorStyles.helpBox, GUILayout.ExpandHeight(true));
            var messages = ChatService.Messages;
            if (messages.Count == 0) GUILayout.Label("<color=#888888>No messages yet. Say hi!</color>", _chatStyle);
            foreach (var msg in messages)
            {
                var time = DateTimeOffset.FromUnixTimeMilliseconds(msg.t).ToLocalTime().ToString("HH:mm");
                var color = string.IsNullOrEmpty(msg.color) ? "#FFFFFF" : msg.color;
                GUILayout.Label($"<color=#888888>{time}</color> <b><color={color}>{Escape(msg.name)}</color></b>  {Escape(msg.text)}", _chatStyle);
            }
            if (_scrollChatToEnd && Event.current.type == EventType.Repaint)
            {
                _chatScroll.y = float.MaxValue;
                _scrollChatToEnd = false;
            }
            EditorGUILayout.EndScrollView();

            using (new EditorGUILayout.HorizontalScope())
            {
                bool enter = Event.current.type == EventType.KeyDown
                             && (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter)
                             && GUI.GetNameOfFocusedControl() == "CollabChatInput";
                if (enter)
                {
                    SendDraft();
                    Event.current.Use();
                }
                using (new EditorGUI.DisabledScope(!CollabSession.IsConnected))
                {
                    GUI.SetNextControlName("CollabChatInput");
                    _draft = EditorGUILayout.TextField(_draft);
                    if (_refocus && Event.current.type == EventType.Repaint)
                    {
                        _refocus = false;
                        GUI.FocusControl("CollabChatInput");
                        Repaint();
                    }
                    if (GUILayout.Button("Send", GUILayout.Width(50))) SendDraft();
                }
            }
        }

        private void SendDraft()
        {
            if (string.IsNullOrWhiteSpace(_draft)) return;
            ChatService.Send(_draft);
            _draft = "";
            // IMGUI keeps a focused text field's old text: drop focus, then take it back next repaint.
            GUI.FocusControl(null);
            _refocus = true;
            _scrollChatToEnd = true;
            Repaint();
        }

        #endregion

        /// <summary>Stops people's text from being read as rich-text tags.</summary>
        private static string Escape(string text) => (text ?? "").Replace("<", "<​");
    }
}
