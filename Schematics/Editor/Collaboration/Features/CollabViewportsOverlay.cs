using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Overlays;
using UnityEditor.Toolbars;
using UnityEngine;
using UnityEngine.UIElements;

namespace Remedy.Schematics.Collaboration
{
    /// <summary>
    /// A row of camera buttons along the bottom of the Scene view, one per teammate (in their colour). Clicking one opens
    /// (or closes) a viewport showing what they're looking at, in the <see cref="CollabViewportsPanelOverlay"/>. Only shown
    /// while you're in a session with someone else.
    /// </summary>
    [Overlay(typeof(SceneView), Id, "Teammate Viewports", true,
        defaultDockZone = DockZone.BottomToolbar, defaultDockPosition = DockPosition.Bottom)]
    public sealed class CollabViewportsButtonsOverlay : Overlay, ITransientOverlay, ICreateHorizontalToolbar, ICreateVerticalToolbar
    {
        public const string Id = "schematics-collab-viewport-buttons";

        public bool visible => CollabSession.IsConnected && CollabFeatures.RemotePeers().Any();

        public override VisualElement CreatePanelContent() => new ViewportButtonStrip(false);

        public OverlayToolbar CreateHorizontalToolbarContent()
        {
            var toolbar = new OverlayToolbar();
            toolbar.Add(new ViewportButtonStrip(false));
            return toolbar;
        }

        public OverlayToolbar CreateVerticalToolbarContent()
        {
            var toolbar = new OverlayToolbar();
            toolbar.Add(new ViewportButtonStrip(true));
            return toolbar;
        }

        /// <summary>The buttons themselves; rebuilds only when the people (or their state) change.</summary>
        private sealed class ViewportButtonStrip : VisualElement
        {
            private readonly bool _vertical;
            private string _signature;
            private static Texture2D _cameraIcon;

            public ViewportButtonStrip(bool vertical)
            {
                _vertical = vertical;
                style.flexDirection = vertical ? FlexDirection.Column : FlexDirection.Row;
                style.alignItems = Align.Center;
                RegisterCallback<AttachToPanelEvent>(_ => { CollabViewports.OpenChanged += Refresh; Refresh(); });
                RegisterCallback<DetachFromPanelEvent>(_ => CollabViewports.OpenChanged -= Refresh);
                schedule.Execute(Refresh).Every(500);
            }

            private void Refresh() => CollabFeatures.Safe(Rebuild);

            private void Rebuild()
            {
                var peers = CollabSession.IsConnected ? CollabFeatures.RemotePeers().ToList() : new List<CollabPeer>();
                var signature = new StringBuilder();
                foreach (var peer in peers)
                    signature.Append(peer.Id).Append('|').Append(peer.Name).Append('|').Append(CollabFeatures.Hex(peer.Color)).Append('|')
                        .Append(CollabViewports.IsOpen(peer.Id)).Append('|').Append(CollabViewports.Unavailable(peer, out _)).Append('|')
                        .Append(CollabViewports.SceneOf(peer)).Append('\n');
                if (signature.ToString() == _signature) return;
                _signature = signature.ToString();

                Clear();
                if (peers.Count == 0)
                {
                    Add(new Label(CollabSession.IsConnected ? "No teammates online" : "Not connected") { style = { unityTextAlign = TextAnchor.MiddleCenter, paddingLeft = 4, paddingRight = 4 } });
                    return;
                }
                foreach (var peer in peers) Add(MakeButton(peer));
            }

            private VisualElement MakeButton(CollabPeer peer)
            {
                string peerId = peer.Id;
                bool open = CollabViewports.IsOpen(peerId);
                _cameraIcon ??= EditorGUIUtility.IconContent("Camera Icon")?.image as Texture2D;

                var button = new EditorToolbarButton(_vertical ? "" : peer.Name, _cameraIcon, () => CollabViewports.Toggle(peerId));

                // Their colour: a small stripe under (or beside) the button, plus a tint while their viewport is open.
                var color = peer.Color;
                if (_vertical) { button.style.borderLeftWidth = 3; button.style.borderLeftColor = color; }
                else { button.style.borderBottomWidth = 3; button.style.borderBottomColor = color; }
                if (open) button.style.backgroundColor = new Color(color.r, color.g, color.b, 0.35f);

                var tip = new StringBuilder(peer.Name);
                var scene = CollabViewports.SceneOf(peer);
                if (!string.IsNullOrEmpty(scene)) tip.Append("\nScene: ").Append(scene);
                string reason = CollabViewports.Unavailable(peer, out _);
                if (reason != null) tip.Append('\n').Append(reason);
                tip.Append(open ? "\nClick to close their viewport." : "\nClick to see what they see.");
                button.tooltip = tip.ToString();
                if (reason != null && !open) button.style.opacity = 0.6f;
                return button;
            }
        }
    }

    /// <summary>
    /// Holds the open viewports: one tile per teammate, each with their name, a close button and a resize grip in the
    /// corner. It's an ordinary Scene view overlay, so it can be dragged out to float anywhere or docked to a side, and it
    /// disappears when no viewport is open. The pictures are rendered by <see cref="CollabViewports"/>.
    /// </summary>
    [Overlay(typeof(SceneView), Id, "Viewports", true,
        defaultDockZone = DockZone.RightColumn, defaultDockPosition = DockPosition.Bottom)]
    public sealed class CollabViewportsPanelOverlay : Overlay, ITransientOverlay
    {
        public const string Id = "schematics-collab-viewports";

        public bool visible => CollabSession.IsConnected && CollabViewports.Open.Count > 0;

        public override VisualElement CreatePanelContent()
        {
            var root = new VisualElement();
            root.style.flexDirection = FlexDirection.Row;
            root.style.flexWrap = Wrap.Wrap;
            root.style.alignItems = Align.FlexStart;
            root.style.maxWidth = 1400;

            System.Action rebuild = () => CollabFeatures.Safe(() => Rebuild(root));
            root.RegisterCallback<AttachToPanelEvent>(_ => { CollabViewports.OpenChanged += rebuild; rebuild(); });
            root.RegisterCallback<DetachFromPanelEvent>(_ => CollabViewports.OpenChanged -= rebuild);
            return root;
        }

        private void Rebuild(VisualElement root)
        {
            var existing = root.Children().OfType<ViewportTile>().ToList();
            var wanted = CollabViewports.Open.Select(v => v.PeerId).ToList();
            if (existing.Select(t => t.PeerId).SequenceEqual(wanted)) return;
            root.Clear();
            foreach (var id in wanted) root.Add(new ViewportTile(this, id));
        }
    }

    /// <summary>One viewport in the panel: header (name, close), the picture, and a resize grip.</summary>
    internal sealed class ViewportTile : VisualElement, CollabViewports.IViewportTile
    {
        private const float DefaultWidth = 320f, MinWidth = 160f, MaxWidth = 1280f;
        private const string WidthKey = "Remedy.Schematics.CollabViewports.Width";

        private readonly Overlay _overlay;
        private readonly VisualElement _stripe, _body;
        private readonly Label _title, _status;
        private readonly Image _image;
        private float _width;
        private float _aspect = 16f / 9f;

        public string PeerId { get; }

        public ViewportTile(Overlay overlay, string peerId)
        {
            _overlay = overlay;
            PeerId = peerId;
            _width = Mathf.Clamp(EditorPrefs.GetFloat(WidthKey, DefaultWidth), MinWidth, MaxWidth);
            style.marginLeft = style.marginRight = style.marginTop = style.marginBottom = 2;

            // Header: colour stripe, name, close.
            var header = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, height = 20 } };
            _stripe = new VisualElement { style = { width = 4, alignSelf = Align.Stretch, marginRight = 4 } };
            header.Add(_stripe);
            _title = new Label { style = { flexGrow = 1, unityFontStyleAndWeight = FontStyle.Bold, overflow = Overflow.Hidden, textOverflow = TextOverflow.Ellipsis } };
            header.Add(_title);
            var close = new Button(() => CollabViewports.Close(PeerId)) { text = "×", tooltip = "Close this viewport" };
            close.style.width = 18;
            close.style.height = 16;
            close.style.paddingLeft = close.style.paddingRight = 0;
            close.style.marginRight = 0;
            header.Add(close);
            Add(header);

            // Body: the picture, letterboxed, with a message on top when it can't render.
            _body = new VisualElement { style = { backgroundColor = new Color(0.1f, 0.1f, 0.1f, 1f), overflow = Overflow.Hidden } };
            _image = new Image { scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
            _image.style.position = Position.Absolute;
            _image.style.left = _image.style.top = _image.style.right = _image.style.bottom = 0;
            _body.Add(_image);
            _status = new Label { pickingMode = PickingMode.Ignore };
            _status.style.position = Position.Absolute;
            _status.style.left = _status.style.top = _status.style.right = _status.style.bottom = 0;
            _status.style.unityTextAlign = TextAnchor.MiddleCenter;
            _status.style.whiteSpace = WhiteSpace.Normal;
            _status.style.color = new Color(0.8f, 0.8f, 0.8f, 1f);
            _body.Add(_status);
            _body.Add(MakeResizeGrip());
            Add(_body);

            RegisterCallback<AttachToPanelEvent>(_ => CollabViewports.Register(this));
            RegisterCallback<DetachFromPanelEvent>(_ => CollabViewports.Unregister(this));
            schedule.Execute(() => CollabFeatures.Safe(RefreshInfo)).Every(500);
            RefreshInfo();
            OnRendered();
        }

        /// <summary>On screen: attached, its overlay shown and expanded, and its Scene view open.</summary>
        public bool IsShowing =>
            panel != null && _overlay != null && _overlay.displayed && !_overlay.collapsed && _overlay.containerWindow != null &&
            resolvedStyle.display != DisplayStyle.None;

        public Vector2Int PixelSize
        {
            get
            {
                var rect = _body.contentRect;
                float w = float.IsFinite(rect.width) && rect.width > 0f ? rect.width : _width;
                float h = float.IsFinite(rect.height) && rect.height > 0f ? rect.height : _width / _aspect;
                float scale = EditorGUIUtility.pixelsPerPoint;
                return new Vector2Int(Mathf.RoundToInt(w * scale), Mathf.RoundToInt(h * scale));
            }
        }

        public void OnRendered()
        {
            var viewport = CollabViewports.Get(PeerId);
            var texture = viewport?.Texture;
            if (_image.image != texture) _image.image = texture;
            string status = viewport?.Status;
            _status.text = status ?? "";
            _status.style.display = string.IsNullOrEmpty(status) ? DisplayStyle.None : DisplayStyle.Flex;
            _image.style.opacity = string.IsNullOrEmpty(status) ? 1f : 0.35f;   // keep the last picture faintly under the message
            _image.MarkDirtyRepaint();
            // The overlay lives inside the Scene view: it must repaint for the new picture to appear.
            _overlay?.containerWindow?.Repaint();
        }

        private void RefreshInfo()
        {
            var peer = CollabSession.Find(PeerId);
            if (peer != null)
            {
                _title.text = peer.Name;
                _stripe.style.backgroundColor = peer.Color;
                var scene = CollabViewports.SceneOf(peer);
                _title.tooltip = string.IsNullOrEmpty(scene) ? peer.Name : peer.Name + "\nScene: " + scene;
            }
            _aspect = Mathf.Clamp(CollabViewports.AspectOf(peer), 0.25f, 4f);
            ApplySize();
        }

        private void ApplySize()
        {
            _body.style.width = _width;
            _body.style.height = Mathf.Round(_width / _aspect);
            style.width = _width;
        }

        private VisualElement MakeResizeGrip()
        {
            var grip = new VisualElement { tooltip = "Drag to resize" };
            grip.style.position = Position.Absolute;
            grip.style.right = 0;
            grip.style.bottom = 0;
            grip.style.width = 12;
            grip.style.height = 12;
            grip.style.borderTopLeftRadius = 2;
            grip.style.backgroundColor = new Color(1f, 1f, 1f, 0.25f);

            bool dragging = false;
            float startWidth = 0f, startX = 0f;
            grip.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0) return;
                dragging = true;
                startWidth = _width;
                startX = evt.position.x;
                grip.CapturePointer(evt.pointerId);
                evt.StopPropagation();
            });
            grip.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (!dragging || !grip.HasPointerCapture(evt.pointerId)) return;
                _width = Mathf.Clamp(startWidth + (evt.position.x - startX), MinWidth, MaxWidth);
                ApplySize();
                evt.StopPropagation();
            });
            grip.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (!dragging) return;
                dragging = false;
                if (grip.HasPointerCapture(evt.pointerId)) grip.ReleasePointer(evt.pointerId);
                EditorPrefs.SetFloat(WidthKey, _width);   // the next viewport opens at the size you last chose
                evt.StopPropagation();
            });
            return grip;
        }
    }
}
