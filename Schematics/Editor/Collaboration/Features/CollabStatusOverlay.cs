using UnityEditor;
using UnityEditor.Overlays;
using UnityEngine;
using UnityEngine.UIElements;

namespace Remedy.Schematics.Collaboration
{
    /// <summary>
    /// A small Scene view overlay showing the connection state and how many people are in the session. Click it to
    /// open the Team window. (The main toolbar can't be extended through public API in every Unity version, so the
    /// indicator lives here and in the Team window's header.)
    /// </summary>
    [Overlay(typeof(SceneView), "schematics-collaboration", "Collaboration", true)]
    public sealed class CollabStatusOverlay : Overlay
    {
        private Label _label;
        private VisualElement _dot;

        public override VisualElement CreatePanelContent()
        {
            var root = new VisualElement();
            root.style.flexDirection = FlexDirection.Row;
            root.style.alignItems = Align.Center;
            root.style.paddingLeft = 4;
            root.style.paddingRight = 4;
            root.tooltip = "Open the Team window (Window > Schematics > Team)";

            _dot = new VisualElement();
            _dot.style.width = 8;
            _dot.style.height = 8;
            _dot.style.marginRight = 5;
            _dot.style.borderTopLeftRadius = _dot.style.borderTopRightRadius = _dot.style.borderBottomLeftRadius = _dot.style.borderBottomRightRadius = 4;
            root.Add(_dot);

            _label = new Label();
            root.Add(_label);

            root.RegisterCallback<ClickEvent>(_ => TeamWindow.Open());
            root.schedule.Execute(Refresh).Every(500);
            Refresh();
            return root;
        }

        private void Refresh()
        {
            if (_label == null) return;
            bool connected = CollabSession.IsConnected;
            int count = connected ? CollabSession.Peers?.Count ?? 0 : 0;
            string text = connected ? $"{count} online" : CollabStatus.Message;
            if (connected && CollabSession.IsHost) text += " · host";
            if (connected && EditorApplication.isPlayingOrWillChangePlaymode) text += " · paused";
            _label.text = text;
            _dot.style.backgroundColor = CollabStatus.Color;
        }
    }
}
