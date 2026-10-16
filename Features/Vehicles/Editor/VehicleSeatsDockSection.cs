using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace Remedy.Vehicles.Editor
{
    /// <summary>
    /// Vehicle Seats' section in the IO Dock: every seat with its colour, name, driver toggle, a button to select its
    /// anchor, delete, and its four Signals as ordinary dock rows (drag them into the graph to Send or react, like any
    /// Behavior Signal). "+ Seat" makes the anchor, places it, and creates the Signals in one click.
    /// </summary>
    /// <remarks>
    /// Edits go through <see cref="VehicleSeatEditing"/>: straight into Prefab Mode (with Undo) when the prefab is open
    /// there, otherwise into the prefab file. Seat details (exit point, occupant options, pose) are edited in Prefab
    /// Mode's Inspector; the Scene view has handles for anchors and exits.
    /// </remarks>
    internal static class VehicleSeatsDockSection
    {
        [InitializeOnLoadMethod]
        private static void Register() => BehaviorDockSections.Register(typeof(VehicleSeats), Create);

        private static VisualElement Create(BehaviorDockContext context)
        {
            var root = new VisualElement();
            root.style.marginLeft = 4;
            root.style.marginRight = 4;
            root.style.marginBottom = 4;
            Build(root, context);
            return root;
        }

        private static void Build(VisualElement root, BehaviorDockContext context)
        {
            root.Clear();
            root.Add(DockRows.SectionLabel("Seats"));

            var scope = VehicleSeatEditing.Read(PrefabRoot(context), context.Path);
            if (scope == null)
            {
                root.Add(new HelpBox("Vehicle Seats isn't saved on this object yet.", HelpBoxMessageType.None));
                return;
            }

            var seats = scope.Seats;
            if (seats.Length == 0)
                root.Add(new HelpBox("No seats yet. Add one: the first is the driver's.", HelpBoxMessageType.None));

            for (int i = 0; i < seats.Length; i++)
                if (seats[i] != null)
                    root.Add(SeatBox(root, context, scope, i, seats[i]));

            var buttons = new VisualElement();
            buttons.style.flexDirection = FlexDirection.Row;
            buttons.style.marginTop = 4;
            buttons.Add(MakeButton("+ Seat", "Adds a seat: a child anchor next to the others, an exit on the outside, and its Signals.",
                () => Apply(root, context, "Add Seat", s => { VehicleSeatEditing.AddSeat(s); return true; }), grow: true));
            buttons.Add(MakeButton("Tidy Signals", "Deletes Signals left behind by removed seats.",
                () => Apply(root, context, "Tidy Seat Signals", s => { VehicleSeatEditing.TidySignals(s); return false; })));
            buttons.Add(MakeButton("Details…", "Opens the prefab: edit exits, occupant options and poses in the Inspector, and move seats with the Scene view handles.",
                () => OpenPrefab(context, null)));
            root.Add(buttons);
        }

        private static VisualElement SeatBox(VisualElement root, BehaviorDockContext context, VehicleSeatEditing.Scope scope, int index, Seat seat)
        {
            var color = VehicleSeats.SeatColor(index);
            var box = new VisualElement();
            box.style.marginTop = 4;
            box.style.paddingLeft = 4;
            box.style.borderLeftWidth = 3;
            box.style.borderLeftColor = color;

            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;

            var number = new Label(index.ToString());
            number.style.color = color;
            number.style.unityFontStyleAndWeight = FontStyle.Bold;
            number.style.width = 14;
            header.Add(number);

            var name = new TextField { value = seat.Name, isDelayed = true, tooltip = "The seat's name (also renames its anchor and Signals)." };
            name.style.flexGrow = 1;
            name.style.minWidth = 40;
            name.RegisterValueChangedCallback(evt =>
                Apply(root, context, "Rename Seat", s => { VehicleSeatEditing.RenameSeat(s, index, evt.newValue); return true; }));
            header.Add(name);

            var driver = new Toggle { value = seat.IsDriver, text = "Driver", tooltip = "Whoever sits here controls the vehicle." };
            driver.RegisterValueChangedCallback(evt =>
                Apply(root, context, "Toggle Driver Seat", s => { if (index >= s.Seats.Length) return false; s.Seats[index].IsDriver = evt.newValue; return true; }));
            header.Add(driver);

            var anchorPath = seat.Anchor != null ? AnimationUtility.CalculateTransformPath(seat.Anchor, scope.Root.transform) : null;
            var select = MakeButton("◎", "Select the seat's anchor (opens the prefab).", () => OpenPrefab(context, anchorPath));
            select.SetEnabled(anchorPath != null);
            header.Add(select);

            header.Add(MakeButton("×", "Delete this seat (and its anchor). Its Signals stay until Tidy Signals.", () =>
            {
                if (EditorUtility.DisplayDialog("Delete Seat", $"Delete the seat \"{seat.Name}\" and its anchor?", "Delete", "Cancel"))
                    Apply(root, context, "Delete Seat", s => { VehicleSeatEditing.RemoveSeat(s, index); return true; });
            }));
            box.Add(header);

            var detail = new Label($"{(seat.Anchor != null ? seat.Anchor.name : "no anchor (sits at the vehicle)")} · exit {(seat.ExitPoint != null ? seat.ExitPoint.name : seat.ExitOffset.ToString("0.#"))}");
            detail.style.color = new Color(0.6f, 0.6f, 0.6f);
            detail.style.fontSize = 10;
            box.Add(detail);

            // The seat's Signals, as the same draggable rows the dock uses for Behavior Signals.
            AddSignalRow(box, context, seat.OnEntered, SignalDirection.Output, "On Entered", "GameObject Occupant", "Sent when someone sits here.");
            AddSignalRow(box, context, seat.OnExited, SignalDirection.Output, "On Exited", "GameObject Occupant", "Sent when someone leaves this seat.");
            AddSignalRow(box, context, seat.EnterRequest, SignalDirection.Input, "Enter Request", "GameObject Occupant", "Send it to put someone in this seat.");
            AddSignalRow(box, context, seat.ExitRequest, SignalDirection.Input, "Exit Request", "", "Send it to make whoever sits here get out.");

            if (!VehicleSeatEditing.HasAllSignals(seat))
                box.Add(MakeButton("Create Signals", "Creates this seat's On Entered, On Exited, Enter Request and Exit Request.",
                    () => Apply(root, context, "Create Seat Signals", s =>
                    {
                        if (index >= s.Seats.Length) return false;
                        VehicleSeatEditing.CreateSignals(s, s.Seats[index]);
                        return true;
                    })));
            return box;
        }

        private static void AddSignalRow(VisualElement box, BehaviorDockContext context, SignalData signal, SignalDirection direction,
            string label, string parameters, string tooltip)
        {
            if (signal == null)
                return;
            var item = new DockItem
            {
                Kind = DockItem.ItemKind.Signal,
                Path = context.Path,
                Behavior = typeof(VehicleSeats),
                GameObject = context.Target,
                Name = signal.name,
                Signal = signal,
                Direction = direction,
            };
            var icon = direction == SignalDirection.Output ? DockRows.OutputIcon : DockRows.InputIcon;
            string kind = direction == SignalDirection.Output ? "Output" : "Input";
            box.Add(DockRows.Build(item, icon, label, parameters,
                $"{kind} Signal ({signal.name}): {tooltip} Drag it into the graph; click to find the asset.",
                () => EditorGUIUtility.PingObject(signal)));
        }

        private static Button MakeButton(string text, string tooltip, Action click, bool grow = false)
        {
            var button = new Button(click) { text = text, tooltip = tooltip };
            if (grow) button.style.flexGrow = 1;
            return button;
        }

        // The prefab asset the dock shows (reloaded, so it's current after a file save).
        private static GameObject PrefabRoot(BehaviorDockContext context)
        {
            string path = context.Prefab != null ? AssetDatabase.GetAssetPath(context.Prefab) : null;
            var loaded = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<GameObject>(path);
            return loaded != null ? loaded : context.Prefab;
        }

        private static void Apply(VisualElement root, BehaviorDockContext context, string undoName, Func<VehicleSeatEditing.Scope, bool> edit)
        {
            bool changed = VehicleSeatEditing.Edit(PrefabRoot(context), context.Path, undoName, edit);
            // After the change has landed (and the dock has had a chance to rebuild itself), show it.
            EditorApplication.delayCall += () =>
            {
                if (root.panel != null)
                    Build(root, context);
                if (changed)
                    RedrawDock(context);
            };
        }

        // The dock has no public refresh; its Variables rows (e.g. Seats) only update when it's rebuilt.
        private static void RedrawDock(BehaviorDockContext context)
        {
            object window = context.Window;
            if (window == null) return;
            var redraw = window.GetType().GetMethod("RedrawIODock", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            try { redraw?.Invoke(window, new object[] { false }); }
            catch (Exception exception) { Debug.LogException(exception); }
        }

        private static void OpenPrefab(BehaviorDockContext context, string selectPath)
        {
            var prefab = PrefabRoot(context);
            if (prefab == null) return;
            string assetPath = AssetDatabase.GetAssetPath(prefab);
            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage == null || stage.assetPath != assetPath)
                AssetDatabase.OpenAsset(prefab);

            EditorApplication.delayCall += () =>
            {
                var opened = PrefabStageUtility.GetCurrentPrefabStage();
                if (opened == null || opened.assetPath != assetPath) return;
                var root = opened.prefabContentsRoot.transform;
                var target = string.IsNullOrEmpty(selectPath) ? (string.IsNullOrEmpty(context.Path) ? root : root.Find(context.Path)) : root.Find(selectPath);
                if (target != null)
                {
                    Selection.activeGameObject = target.gameObject;
                    SceneView.lastActiveSceneView?.FrameSelected();
                }
            };
        }
    }
}
