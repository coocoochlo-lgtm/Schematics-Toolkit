using System;
using System.Collections.Generic;
using System.Globalization;

// Plain C# (no UnityEngine), compiled by Unity into the runtime assembly and by the SchematicsCollab helper for its self
// test. Keep it to C# 9 / .NET Standard 2.1.

// Filled in by the JSON deserializer or by callers, which the compiler can't see.
#pragma warning disable 0649

namespace Remedy.Schematics.Collaboration
{
    /// <summary>One property value on the "scene" channel: the serialized property path and its value as text.</summary>
    [Serializable]
    internal sealed class LiveEditProp
    {
        public string p;
        public string v;
    }

    /// <summary>A whole component's data in a "comp" message (only its property values are used here).</summary>
    [Serializable]
    internal sealed class LiveEditComponent
    {
        public string type;
        public string key;
        public List<LiveEditProp> props = new List<LiveEditProp>();
    }

    /// <summary>
    /// The part of an editor's scene message (SceneMsg in the editor's Features assembly, documented in
    /// Schematics/Editor/Collaboration/Features/README.md) that a running game can use. Same field names, so the editor's
    /// JSON reads straight into it; the fields a game can't use (hierarchy snapshots, aliases) are simply not declared.
    /// </summary>
    [Serializable]
    internal sealed class LiveEditMsg
    {
        public string op;
        public string scene;
        public string mid;
        public string from;
        public string id;
        public string comp;
        public List<LiveEditProp> props = new List<LiveEditProp>();
        public LiveEditComponent data;
    }

    /// <summary>What a property change does to a live object.</summary>
    internal enum LiveEditTarget
    {
        GameObjectActive,
        GameObjectName,
        GameObjectLayer,
        LocalPosition,
        LocalRotation,
        LocalScale,
        ComponentEnabled,
        /// <summary>Any other serialized field of a component, set by reflection (best effort).</summary>
        Field,
    }

    /// <summary>One change for one object, decoded from a scene message.</summary>
    internal sealed class LiveEditChange
    {
        public string ObjectId;
        /// <summary>"" for the GameObject itself, else "TypeFullName:index" among same-typed components.</summary>
        public string Component;
        public LiveEditTarget Target;
        /// <summary>The serialized property path (for <see cref="LiveEditTarget.Field"/>).</summary>
        public string Path;
        /// <summary>-1 for the whole value, 0..3 for one of x, y, z, w (paths like "m_LocalPosition.x").</summary>
        public int Axis = -1;
        public string Value;

        public override string ToString() => ObjectId + " " + Component + " " + Target + (Axis >= 0 ? "[" + Axis + "]" : "") + " " + Path + " = " + Value;
    }

    /// <summary>
    /// Reads the editors' live scene edits for a running game: which object, which part of it, which value. The value
    /// text is the editor's PropertyCodec format (numbers in invariant culture, vectors as "x,y,z", bools as 1/0,
    /// colours as "r,g,b,a"); object references ("a:..." assets, "s:..." scene objects) can't be resolved in a build and
    /// are skipped.
    /// </summary>
    internal static class CollabLiveEditCodec
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <summary>The changes a scene message makes that a running game can apply (empty for structural changes).</summary>
        public static List<LiveEditChange> Changes(LiveEditMsg msg)
        {
            var list = new List<LiveEditChange>();
            if (msg == null || string.IsNullOrEmpty(msg.id)) return list;
            string comp = msg.comp ?? "";
            List<LiveEditProp> props;
            switch (msg.op)
            {
                case "props":
                    props = msg.props;
                    break;
                case "comp":
                    props = msg.data?.props;
                    if (string.IsNullOrEmpty(comp)) comp = msg.data?.key ?? "";
                    break;
                case "parent":
                    // Re-parenting isn't mirrored in a running game, but the new local transform is.
                    props = msg.props;
                    comp = "UnityEngine.Transform:0";
                    break;
                default:
                    return list;   // create / structure / destroy / committed: hierarchy changes stay in the editors
            }
            if (props == null) return list;
            bool isTransform = IsTransform(comp);
            foreach (var prop in props)
            {
                if (prop == null || string.IsNullOrEmpty(prop.p)) continue;
                if (IsReference(prop.v)) continue;
                var change = Classify(comp, isTransform, prop.p);
                if (change == null) continue;
                change.ObjectId = msg.id;
                change.Component = comp;
                change.Value = prop.v ?? "";
                list.Add(change);
            }
            return list;
        }

        public static bool IsTransform(string componentKey) =>
            componentKey != null && (componentKey.StartsWith("UnityEngine.Transform:", StringComparison.Ordinal)
                                     || componentKey.StartsWith("UnityEngine.RectTransform:", StringComparison.Ordinal));

        private static bool IsReference(string value) =>
            value != null && (value.StartsWith("a:", StringComparison.Ordinal) || value.StartsWith("s:", StringComparison.Ordinal));

        private static LiveEditChange Classify(string comp, bool isTransform, string path)
        {
            SplitAxis(path, out string root, out int axis);
            if (string.IsNullOrEmpty(comp))
            {
                switch (path)
                {
                    case "m_IsActive": return new LiveEditChange { Target = LiveEditTarget.GameObjectActive, Path = path };
                    case "m_Name": return new LiveEditChange { Target = LiveEditTarget.GameObjectName, Path = path };
                    case "m_Layer": return new LiveEditChange { Target = LiveEditTarget.GameObjectLayer, Path = path };
                    default: return null;   // tag, static flags, icons...: editor-side only
                }
            }
            if (isTransform)
            {
                switch (root)
                {
                    case "m_LocalPosition": return new LiveEditChange { Target = LiveEditTarget.LocalPosition, Path = path, Axis = axis };
                    case "m_LocalRotation": return new LiveEditChange { Target = LiveEditTarget.LocalRotation, Path = path, Axis = axis };
                    case "m_LocalScale": return new LiveEditChange { Target = LiveEditTarget.LocalScale, Path = path, Axis = axis };
                    default: return null;   // m_LocalEulerAnglesHint and RectTransform layout: not mirrored
                }
            }
            if (path == "m_Enabled") return new LiveEditChange { Target = LiveEditTarget.ComponentEnabled, Path = path };
            return new LiveEditChange { Target = LiveEditTarget.Field, Path = path };
        }

        /// <summary>"m_LocalPosition.y" -> ("m_LocalPosition", 1); anything else -> (path, -1).</summary>
        public static void SplitAxis(string path, out string root, out int axis)
        {
            root = path;
            axis = -1;
            if (path == null || path.Length < 3 || path[path.Length - 2] != '.') return;
            switch (path[path.Length - 1])
            {
                case 'x': axis = 0; break;
                case 'y': axis = 1; break;
                case 'z': axis = 2; break;
                case 'w': axis = 3; break;
                default: return;
            }
            root = path.Substring(0, path.Length - 2);
        }

        #region Values

        public static bool TryBool(string v, out bool value)
        {
            value = v == "1" || string.Equals(v, "true", StringComparison.OrdinalIgnoreCase);
            return v == "1" || v == "0" || string.Equals(v, "true", StringComparison.OrdinalIgnoreCase) || string.Equals(v, "false", StringComparison.OrdinalIgnoreCase);
        }

        public static bool TryDouble(string v, out double value) =>
            double.TryParse(v, NumberStyles.Float, Inv, out value) && !double.IsNaN(value) && !double.IsInfinity(value);

        public static bool TryLong(string v, out long value) => long.TryParse(v, NumberStyles.Integer, Inv, out value);

        /// <summary>"x,y,z" -> floats (missing parts are 0, like the editor's codec). False if any part isn't a finite number.</summary>
        public static bool TryFloats(string v, int count, out float[] values)
        {
            values = new float[count];
            if (string.IsNullOrEmpty(v)) return false;
            var parts = v.Split(',');
            for (int i = 0; i < count && i < parts.Length; i++)
            {
                if (!float.TryParse(parts[i], NumberStyles.Float, Inv, out float f) || float.IsNaN(f) || float.IsInfinity(f)) return false;
                values[i] = f;
            }
            return true;
        }

        /// <summary>
        /// Makes a rotation safe to use: NaN/infinite parts or a (near) zero length become the identity, anything else is
        /// normalized. Unity logs "Quaternion To Matrix conversion failed because input Quaternion is invalid" (and the
        /// object or Scene view drawing breaks) for a zero or unnormalized quaternion, so every rotation that arrives from
        /// another peer goes through this. Returns false when it had to fall back to the identity.
        /// </summary>
        public static bool Normalize(ref float x, ref float y, ref float z, ref float w)
        {
            if (!Finite(x) || !Finite(y) || !Finite(z) || !Finite(w))
            {
                x = y = z = 0f;
                w = 1f;
                return false;
            }
            double length = Math.Sqrt((double)x * x + (double)y * y + (double)z * z + (double)w * w);
            if (length < 1e-6)
            {
                x = y = z = 0f;
                w = 1f;
                return false;
            }
            if (Math.Abs(length - 1.0) > 1e-5)
            {
                x = (float)(x / length);
                y = (float)(y / length);
                z = (float)(z / length);
                w = (float)(w / length);
            }
            return true;
        }

        private static bool Finite(float f) => !float.IsNaN(f) && !float.IsInfinity(f);

        #endregion
    }
}
