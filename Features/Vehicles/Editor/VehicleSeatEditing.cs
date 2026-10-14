using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Remedy.Framework;
using Remedy.Schematics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Remedy.Vehicles.Editor
{
    /// <summary>
    /// Edits a vehicle's seats wherever it lives: in a scene or in Prefab Mode it's edited in place (with Undo), and a
    /// prefab that isn't open is loaded, edited and saved (like the IO Dock does). Used by the IO Dock section, the
    /// Scene view handles and the Create Arcade Car menu, so a seat is always made the same way: a child anchor,
    /// a sensible position, and its four Signals.
    /// </summary>
    public static class VehicleSeatEditing
    {
        public const string SeatsVariable = "Seats";

        /// <summary>One vehicle being edited: its seats (a copy, written back by <see cref="Commit"/>) and where they live.</summary>
        public sealed class Scope
        {
            /// <summary>The Schematic Object's GameObject (the prefab root).</summary>
            public GameObject Root;
            /// <summary>The GameObject Vehicle Seats is attached to.</summary>
            public GameObject Target;
            public SchematicObject Schematic;
            public BehaviorAttachment Attachment;
            /// <summary>True in a scene or Prefab Mode, where Undo works; false for a loaded copy of a prefab file.</summary>
            public bool Live;
            /// <summary>The prefab file, if there is one (the seat Signals asset goes next to it).</summary>
            public string AssetPath;
            public Seat[] Seats;

            public Transform CreateChild(Transform parent, string name, Vector3 localPosition)
            {
                var child = new GameObject(name).transform;
                child.SetParent(parent, false);
                child.localPosition = localPosition;
                if (Live)
                    Undo.RegisterCreatedObjectUndo(child.gameObject, "Add Seat");
                return child;
            }

            public void Destroy(GameObject gameObject)
            {
                if (gameObject == null) return;
                Schematic.Behaviors.Remove(gameObject);
                if (Live) Undo.DestroyObjectImmediate(gameObject);
                else Object.DestroyImmediate(gameObject);
            }

            public void Commit() => Attachment.SetOverride(SeatsVariable, Seats, typeof(Seat[]));
        }

        /// <summary>
        /// Opens a vehicle for reading: from Prefab Mode if <paramref name="root"/>'s prefab is open there (so the
        /// newest edits show), otherwise from <paramref name="root"/> itself. Null if it has no Vehicle Seats.
        /// </summary>
        public static Scope Read(GameObject root, string path)
        {
            if (root == null) return null;
            if (EditorUtility.IsPersistent(root))
            {
                var stage = PrefabStageUtility.GetCurrentPrefabStage();
                string assetPath = AssetDatabase.GetAssetPath(root);
                if (stage != null && stage.assetPath == assetPath)
                    return Open(stage.prefabContentsRoot, path, true, assetPath);
                return Open(root, path, false, assetPath);
            }
            return Open(root, path, true, AssetPathOf(root));
        }

        /// <summary>
        /// Runs an edit on a vehicle's seats and saves it. <paramref name="root"/> is the Schematic Object (a prefab
        /// asset or a scene / Prefab Mode object) and <paramref name="path"/> the object Vehicle Seats is on, from it.
        /// Return false from <paramref name="edit"/> to cancel. Returns true if something was saved.
        /// </summary>
        public static bool Edit(GameObject root, string path, string undoName, Func<Scope, bool> edit)
        {
            if (root == null || edit == null)
                return false;

            if (EditorUtility.IsPersistent(root))
            {
                string assetPath = AssetDatabase.GetAssetPath(root);
                var stage = PrefabStageUtility.GetCurrentPrefabStage();
                if (stage != null && stage.assetPath == assetPath)
                    return EditLive(stage.prefabContentsRoot, path, undoName, edit, assetPath);
                return EditFile(assetPath, path, edit);
            }
            return EditLive(root, path, undoName, edit, AssetPathOf(root));
        }

        private static bool EditLive(GameObject root, string path, string undoName, Func<Scope, bool> edit, string assetPath)
        {
            var scope = Open(root, path, true, assetPath);
            if (scope == null)
                return false;

            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(undoName);
            Undo.RecordObject(scope.Schematic, undoName);
            if (!edit(scope))
                return false;
            scope.Commit();

            EditorUtility.SetDirty(scope.Schematic);
            PrefabUtility.RecordPrefabInstancePropertyModifications(scope.Schematic);
            if (root.scene.IsValid())
                EditorSceneManager.MarkSceneDirty(root.scene);
            Undo.CollapseUndoOperations(group);
            return true;
        }

        private static bool EditFile(string assetPath, string path, Func<Scope, bool> edit)
        {
            if (string.IsNullOrEmpty(assetPath))
                return false;
            var contents = PrefabUtility.LoadPrefabContents(assetPath);
            try
            {
                var scope = Open(contents, path, false, assetPath);
                if (scope == null || !edit(scope))
                    return false;
                scope.Commit();
                PrefabUtility.SaveAsPrefabAsset(contents, assetPath);
                return true;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        private static Scope Open(GameObject root, string path, bool live, string assetPath)
        {
            var target = string.IsNullOrEmpty(path) ? root.transform : root.transform.Find(path);
            if (target == null)
                return null;
            var schematic = root.GetComponent<SchematicObject>();
            if (schematic == null)
                schematic = target.GetComponentInParent<SchematicObject>(true);
            var attachment = schematic != null ? schematic.FindAttachment(typeof(VehicleSeats), target.gameObject) : null;
            if (attachment == null)
                return null;

            return new Scope
            {
                Root = schematic.gameObject,
                Target = target.gameObject,
                Schematic = schematic,
                Attachment = attachment,
                Live = live,
                AssetPath = assetPath,
                Seats = ReadSeats(attachment) ?? Array.Empty<Seat>(),
            };
        }

        /// <summary>The seats an attachment spawns with: its override, else the Behavior Metadata default.</summary>
        public static Seat[] ReadSeats(BehaviorAttachment attachment)
        {
            if (attachment == null) return null;
            var entry = attachment.FindOverride(SeatsVariable);
            if (entry?.Value != null)
                return entry.Value.Get<Seat[]>();
            var definition = attachment.Metadata != null ? attachment.Metadata.FindVariable(SeatsVariable) : null;
            return definition?.Default?.Get<Seat[]>();
        }

        private static string AssetPathOf(GameObject sceneObject)
        {
            var stage = PrefabStageUtility.GetPrefabStage(sceneObject);
            if (stage != null)
                return stage.assetPath;
            return PrefabUtility.IsPartOfPrefabInstance(sceneObject)
                ? PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(sceneObject)
                : null;
        }

        #region Seat operations

        /// <summary>
        /// Adds a seat: a "Seat_&lt;Name&gt;" child anchor placed next to the existing seats (mirrored across the car,
        /// then a row behind), its exit on the outside, and its Signals. The first seat is the driver.
        /// </summary>
        public static Seat AddSeat(Scope scope, string name = null)
        {
            var seats = scope.Seats ?? Array.Empty<Seat>();
            var taken = seats.Where(s => s != null).Select(s => s.Name);
            name = Unique(name ?? (seats.Length == 0 ? "Driver" : seats.Length == 1 ? "Passenger" : "Seat"), taken);

            var car = scope.Target.transform;
            Vector3 local = SuggestPosition(car, seats);
            var anchor = scope.CreateChild(car, "Seat_" + Sanitize(name), local);
            var seat = new Seat(name, anchor, isDriver: !seats.Any(s => s != null && s.IsDriver))
            {
                ExitOffset = new Vector3(local.x >= 0f ? 1.5f : -1.5f, 0f, 0f),
            };
            CreateSignals(scope, seat);
            scope.Seats = seats.Append(seat).ToArray();
            return seat;
        }

        /// <summary>Removes a seat and its anchor (if nothing else lives under it). Its Signals stay until Tidy Signals.</summary>
        public static void RemoveSeat(Scope scope, int index)
        {
            var seats = scope.Seats.ToList();
            if (index < 0 || index >= seats.Count) return;
            var anchor = seats[index]?.Anchor;
            seats.RemoveAt(index);
            scope.Seats = seats.ToArray();
            if (anchor != null && anchor != scope.Target.transform && anchor.childCount == 0 && anchor.GetComponents<Component>().Length == 1)
                scope.Destroy(anchor.gameObject);
        }

        /// <summary>Renames a seat, its anchor and its Signals.</summary>
        public static void RenameSeat(Scope scope, int index, string name)
        {
            var seat = index >= 0 && index < scope.Seats.Length ? scope.Seats[index] : null;
            if (seat == null || string.IsNullOrWhiteSpace(name)) return;
            name = Unique(name.Trim(), scope.Seats.Where(s => s != null && s != seat).Select(s => s.Name));
            string old = seat.Name;
            seat.Name = name;
            if (seat.Anchor != null && seat.Anchor.name == "Seat_" + Sanitize(old))
            {
                if (scope.Live) Undo.RecordObject(seat.Anchor.gameObject, "Rename Seat");
                seat.Anchor.name = "Seat_" + Sanitize(name);
            }
            foreach (var signal in SignalsOf(seat))
            {
                if (signal == null || !signal.name.StartsWith(old + " ")) continue;
                Undo.RecordObject(signal, "Rename Seat");
                signal.name = name + signal.name.Substring(old.Length);
                signal.Name = signal.name;
                EditorUtility.SetDirty(signal);
            }
            AssetDatabase.SaveAssets();
        }

        public static IEnumerable<SignalData> SignalsOf(Seat seat)
        {
            if (seat == null) yield break;
            yield return seat.OnEntered;
            yield return seat.OnExited;
            yield return seat.EnterRequest;
            yield return seat.ExitRequest;
        }

        public static bool HasAllSignals(Seat seat)
            => seat != null && seat.OnEntered != null && seat.OnExited != null && seat.EnterRequest != null && seat.ExitRequest != null;

        private static Vector3 SuggestPosition(Transform car, Seat[] seats)
        {
            var used = seats.Where(s => s?.Anchor != null).Select(s => car.InverseTransformPoint(s.Anchor.position)).ToList();
            if (used.Count == 0)
                return new Vector3(-0.4f, 0.3f, 0.1f);

            bool Free(Vector3 p) => used.All(u => (u - p).sqrMagnitude > 0.3f * 0.3f);
            foreach (var u in used)
            {
                var mirrored = new Vector3(-u.x, u.y, u.z);
                if (Mathf.Abs(u.x) > 0.05f && Free(mirrored))
                    return mirrored;
            }
            var last = used[used.Count - 1];
            var behind = new Vector3(used[0].x, last.y, last.z - 0.9f);
            while (!Free(behind))
                behind.z -= 0.9f;
            return behind;
        }

        public static string Unique(string name, IEnumerable<string> taken)
        {
            var set = new HashSet<string>(taken.Where(t => t != null));
            if (!set.Contains(name)) return name;
            for (int i = 2; ; i++)
                if (!set.Contains($"{name} {i}")) return $"{name} {i}";
        }

        private static string Sanitize(string name) => new string((name ?? "Seat").Where(c => char.IsLetterOrDigit(c) || c == '_').ToArray());

        #endregion

        #region Signals

        /// <summary>
        /// Gives a seat any of its four Signals it's missing, as sub-assets of the vehicle's "Seat Signals" asset: On
        /// Entered and On Exited (Occupant: GameObject), Enter Request (Occupant: GameObject) and Exit Request.
        /// </summary>
        public static void CreateSignals(Scope scope, Seat seat)
        {
            if (seat == null || HasAllSignals(seat)) return;
            var bank = BankFor(scope);
            var occupant = ("Occupant", typeof(GameObject));
            if (seat.OnEntered == null) seat.OnEntered = CreateSignal(bank, seat.Name, "Entered", occupant);
            if (seat.OnExited == null) seat.OnExited = CreateSignal(bank, seat.Name, "Exited", occupant);
            // Requests are networked (anyone may send them; only the vehicle's authority acts on them). On Entered /
            // On Exited stay local: every player fires their own when the synced seating changes.
            if (seat.EnterRequest == null) seat.EnterRequest = Networked(CreateSignal(bank, seat.Name, "Enter Request", occupant));
            if (seat.ExitRequest == null) seat.ExitRequest = Networked(CreateSignal(bank, seat.Name, "Exit Request"));
            EditorUtility.SetDirty(bank);
            AssetDatabase.SaveAssets();
        }

        /// <summary>The Seat Signals asset this vehicle uses: the one its seats' Signals are in, else "&lt;Prefab&gt; Seat Signals.asset" next to it.</summary>
        public static VehicleSeatSignals BankFor(Scope scope)
        {
            foreach (var seat in scope.Seats)
                foreach (var signal in SignalsOf(seat))
                    if (signal != null && AssetDatabase.LoadMainAssetAtPath(AssetDatabase.GetAssetPath(signal)) is VehicleSeatSignals used)
                        return used;

            string folder = "Assets";
            string name = scope.Root != null ? scope.Root.name : "Vehicle";
            if (!string.IsNullOrEmpty(scope.AssetPath))
            {
                folder = Path.GetDirectoryName(scope.AssetPath)?.Replace('\\', '/') ?? "Assets";
                name = Path.GetFileNameWithoutExtension(scope.AssetPath);
            }
            string path = $"{folder}/{name} Seat Signals.asset";
            var existing = AssetDatabase.LoadAssetAtPath<VehicleSeatSignals>(path);
            if (existing != null)
                return existing;

            var bank = ScriptableObject.CreateInstance<VehicleSeatSignals>();
            AssetDatabase.CreateAsset(bank, path);
            return bank;
        }

        private static SignalData Networked(SignalData signal)
        {
            signal.Networked = true;
            signal.Sender = Remedy.Schematics.SignalNetworkSender.Anyone;
            return signal;
        }

        private static SignalData CreateSignal(VehicleSeatSignals bank, string seatName, string suffix, params (string name, Type type)[] parameters)
        {
            var signal = ScriptableObject.CreateInstance<SignalData>();
            signal.name = $"{seatName} {suffix}";
            signal.Name = signal.name;
            foreach (var (parameterName, type) in parameters)
                signal.Parameters.AddParameter(parameterName, new Union { Type = SignalArgs.ValueTypeOf(type) });
            signal.RuntimeArgs = new Union[parameters.Length];
            AssetDatabase.AddObjectToAsset(signal, bank);
            bank.Signals.Add(signal);
            return signal;
        }

        /// <summary>
        /// Deletes the Signals in this vehicle's Seat Signals asset that none of its seats use any more (left behind by
        /// removed seats). Asks first. Returns how many were removed.
        /// </summary>
        public static int TidySignals(Scope scope)
        {
            var bank = BankFor(scope);
            var used = new HashSet<SignalData>(scope.Seats.SelectMany(SignalsOf).Where(s => s != null));
            var unused = bank.Signals.Where(s => s != null && !used.Contains(s)).ToList();
            bank.Signals.RemoveAll(s => s == null);
            if (unused.Count == 0)
                return 0;
            if (!EditorUtility.DisplayDialog("Tidy Seat Signals",
                    $"Delete {unused.Count} Signal(s) no seat of {scope.Root.name} uses?\n\n{string.Join("\n", unused.Select(s => s.name))}\n\n" +
                    "Graph nodes still using them stop working, and other prefabs sharing this asset (copies of this one) lose them too.",
                    "Delete", "Cancel"))
                return 0;
            foreach (var signal in unused)
            {
                bank.Signals.Remove(signal);
                AssetDatabase.RemoveObjectFromAsset(signal);
                Object.DestroyImmediate(signal, true);
            }
            EditorUtility.SetDirty(bank);
            AssetDatabase.SaveAssets();
            return unused.Count;
        }

        #endregion
    }
}
