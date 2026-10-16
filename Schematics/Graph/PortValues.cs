using Remedy.Framework;
using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Remedy.Schematics
{
    /// <summary>
    /// Converts values travelling between graph ports into the type the reading node asked for. Values arrive either as
    /// a <see cref="Union"/> (written by action nodes with SetOutputValue) or as a plain object (returned by a query
    /// node's OnRequestValue).
    /// </summary>
    /// <remarks>
    /// Why this exists: <see cref="Union.GetValue{T}"/> reads raw memory, so asking a Union that holds an int for a
    /// Vector3 gives (int, 0, 0), and asking it for a GameObject reinterprets the number's bits as an object reference
    /// (a crash waiting to happen). Everything here checks what the value actually is first, and only converts when
    /// the conversion makes sense; otherwise it reports "no value" and the node falls back to its default. The common
    /// cases (numbers, vectors, matching references) never allocate.
    /// </remarks>
    public static class PortValues
    {
        private static class Info<T>
        {
            public static readonly bool IsValueType = typeof(T).IsValueType;
            public static readonly bool IsComponent = typeof(Component).IsAssignableFrom(typeof(T));
            public static readonly bool IsEnum = typeof(T).IsEnum;
            public static readonly bool IsConvertible = typeof(IConvertible).IsAssignableFrom(typeof(T));
        }

        /// <summary>True for the Union kinds that hold an object reference.</summary>
        public static bool IsReference(Union.ValueType type)
        {
            switch (type)
            {
                case Union.ValueType.String:
                case Union.ValueType.GameObject:
                case Union.ValueType.Transform:
                case Union.ValueType.Component:
                case Union.ValueType.Material:
                case Union.ValueType.Texture:
                case Union.ValueType.AudioClip:
                case Union.ValueType.Array:
                case Union.ValueType.Object:
                case Union.ValueType.PhysicsMaterial:
                case Union.ValueType.List:
                case Union.ValueType.Dictionary:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>True for the Union kinds stored as numbers (a Vector4 under the hood).</summary>
        public static bool IsNumeric(Union.ValueType type)
        {
            switch (type)
            {
                case Union.ValueType.Bool:
                case Union.ValueType.Int:
                case Union.ValueType.Float:
                case Union.ValueType.Vector2:
                case Union.ValueType.Vector3:
                case Union.ValueType.Vector4:
                case Union.ValueType.Color:
                case Union.ValueType.Color32:
                case Union.ValueType.Quaternion:
                case Union.ValueType.LayerMask:
                    return true;
                default:
                    return false;
            }
        }

        private static bool IsScalar(Union.ValueType type)
            => type == Union.ValueType.Bool || type == Union.ValueType.Int || type == Union.ValueType.Float || type == Union.ValueType.LayerMask;

        /// <summary>The object a Union refers to, or null when it holds a number/vector (or nothing).</summary>
        public static object ReferenceOf(Union value) => IsReference(value.Type) ? value.GetValue<object>() : null;

        /// <summary>Any value as a Union (boxes are unwrapped to their natural Union kind).</summary>
        public static Union ToUnion(object value) => value is Union union ? union : SignalArgs.ToUnion<object>(value);

        /// <summary>
        /// Reads a Union as <typeparamref name="T"/>. False (and default) when it holds nothing, or something that
        /// can't sensibly become a <typeparamref name="T"/> (a number where an object is wanted, for example).
        /// </summary>
        public static bool TryFromUnion<T>(Union union, out T value)
        {
            if (typeof(T) == typeof(Union))
            {
                value = UnsafeUtility.As<Union, T>(ref union);
                return true;
            }

            var kind = union.Type;
            if (kind == Union.ValueType.Null)
            {
                value = default;
                return !Info<T>.IsValueType;
            }

            if (IsNumeric(kind))
            {
                Vector4 v = union.GetValue<Vector4>();
                bool scalar = IsScalar(kind);

                // Numbers become numbers, vectors become vectors. A number never turns into a vector (that's how an
                // unconnected Position used to read as (1, 0, 0)).
                if (typeof(T) == typeof(float)) { if (!scalar) goto Fail; float f = v.x; value = UnsafeUtility.As<float, T>(ref f); return true; }
                if (typeof(T) == typeof(int)) { if (!scalar) goto Fail; int i = (int)v.x; value = UnsafeUtility.As<int, T>(ref i); return true; }
                if (typeof(T) == typeof(bool)) { if (!scalar) goto Fail; bool b = v.x != 0f; value = UnsafeUtility.As<bool, T>(ref b); return true; }
                if (typeof(T) == typeof(double)) { if (!scalar) goto Fail; double d = v.x; value = UnsafeUtility.As<double, T>(ref d); return true; }
                if (typeof(T) == typeof(IntTag)) { if (!scalar) goto Fail; IntTag tag = new IntTag((int)v.x); value = UnsafeUtility.As<IntTag, T>(ref tag); return true; }
                if (typeof(T) == typeof(LayerMask)) { if (!scalar) goto Fail; LayerMask mask = (int)v.x; value = UnsafeUtility.As<LayerMask, T>(ref mask); return true; }

                if (typeof(T) == typeof(Vector2)) { if (scalar) goto Fail; Vector2 r = v; value = UnsafeUtility.As<Vector2, T>(ref r); return true; }
                if (typeof(T) == typeof(Vector3)) { if (scalar) goto Fail; Vector3 r = v; value = UnsafeUtility.As<Vector3, T>(ref r); return true; }
                if (typeof(T) == typeof(Vector4)) { if (scalar) goto Fail; value = UnsafeUtility.As<Vector4, T>(ref v); return true; }
                if (typeof(T) == typeof(Color)) { if (scalar) goto Fail; Color r = v; value = UnsafeUtility.As<Color, T>(ref r); return true; }
                if (typeof(T) == typeof(Color32)) { if (scalar) goto Fail; Color32 r = (Color)v; value = UnsafeUtility.As<Color32, T>(ref r); return true; }
                if (typeof(T) == typeof(Quaternion)) { if (scalar) goto Fail; Quaternion r = new Quaternion(v.x, v.y, v.z, v.w); value = UnsafeUtility.As<Quaternion, T>(ref r); return true; }

                if (typeof(T) == typeof(string)) { value = (T)(object)union.ToString(); return true; }

                // object, enums, IConvertible...: box it as its natural type and convert from there.
                return TryFromObject(SignalArgs.ToObject(union), out value);
            }

            if (kind == Union.ValueType.Scene)
            {
                if (typeof(T) == typeof(Scene)) { Scene scene = union.GetScene(); value = UnsafeUtility.As<Scene, T>(ref scene); return true; }
                if (typeof(T) == typeof(object)) { value = (T)(object)union.GetScene(); return true; }
                goto Fail;
            }

            if (IsReference(kind))
                return TryFromObject(union.GetValue<object>(), out value);

        // ValueType.Union: a default (never written) Union, or a wrapped one this can't read safely.
        Fail:
            value = default;
            return false;
        }

        /// <summary>
        /// Reads a plain object as <typeparamref name="T"/>: a direct match, a GameObject/Component swap, a number
        /// conversion, a list copy, or IConvertible as a last resort. False when it can't be converted.
        /// </summary>
        public static bool TryFromObject<T>(object source, out T value)
        {
            if (source is T direct)
            {
                value = direct;
                return true;
            }

            if (source == null)
            {
                value = default;
                return !Info<T>.IsValueType;
            }

            if (source is Union union)
                return TryFromUnion(union, out value);

            if (typeof(T) == typeof(Union))
            {
                var converted = SignalArgs.ToUnion<object>(source);
                value = UnsafeUtility.As<Union, T>(ref converted);
                return true;
            }

            // A component where its GameObject is wanted, or the other way around.
            if (typeof(T) == typeof(GameObject))
            {
                if (source is Component component)
                {
                    value = component != null ? (T)(object)component.gameObject : default;
                    return true;
                }
                goto Fail;
            }
            if (Info<T>.IsComponent)
            {
                GameObject owner = source as GameObject;
                if (owner == null && source is Component other && other != null)
                    owner = other.gameObject;
                if (owner != null)
                {
                    value = owner.GetComponent(typeof(T)) is T found ? found : default;
                    return true;
                }
                goto Fail;
            }

            // An old Unity string tag wired (or saved) where an Int Tag is now wanted: look the tag up by name.
            if (typeof(T) == typeof(IntTag) && source is string tagName)
            {
                if (!IntTags.TryGetId(tagName, out int id))
                    goto Fail;
                IntTag tag = new IntTag(id);
                value = UnsafeUtility.As<IntTag, T>(ref tag);
                return true;
            }

            if (TryNumber(source, out double number))
            {
                if (typeof(T) == typeof(float)) { float f = (float)number; value = UnsafeUtility.As<float, T>(ref f); return true; }
                if (typeof(T) == typeof(int)) { int i = (int)number; value = UnsafeUtility.As<int, T>(ref i); return true; }
                if (typeof(T) == typeof(bool)) { bool b = number != 0d; value = UnsafeUtility.As<bool, T>(ref b); return true; }
                if (typeof(T) == typeof(double)) { value = UnsafeUtility.As<double, T>(ref number); return true; }
                if (typeof(T) == typeof(IntTag)) { IntTag tag = new IntTag((int)number); value = UnsafeUtility.As<IntTag, T>(ref tag); return true; }
                if (typeof(T) == typeof(LayerMask)) { LayerMask mask = (int)number; value = UnsafeUtility.As<LayerMask, T>(ref mask); return true; }
                if (Info<T>.IsEnum) { value = (T)Enum.ToObject(typeof(T), (long)number); return true; }
            }

            if (typeof(T) == typeof(Vector3))
            {
                if (source is Vector2 v2) { Vector3 r = v2; value = UnsafeUtility.As<Vector3, T>(ref r); return true; }
                if (source is Vector4 v4) { Vector3 r = v4; value = UnsafeUtility.As<Vector3, T>(ref r); return true; }
            }
            if (typeof(T) == typeof(Vector2))
            {
                if (source is Vector3 v3) { Vector2 r = v3; value = UnsafeUtility.As<Vector2, T>(ref r); return true; }
                if (source is Vector4 v4) { Vector2 r = v4; value = UnsafeUtility.As<Vector2, T>(ref r); return true; }
            }

            if (typeof(T) == typeof(string))
            {
                value = (T)(object)source.ToString();
                return true;
            }

            // Lists of one kind where another is wanted (a List<object> query into a List<Union> input). Copies.
            if (typeof(T) == typeof(List<object>) && source is IEnumerable objects && source is not string)
            {
                var list = new List<object>();
                foreach (var item in objects)
                    list.Add(item is Union u ? SignalArgs.ToObject(u) : item);
                value = (T)(object)list;
                return true;
            }
            if (typeof(T) == typeof(List<Union>) && source is IEnumerable unions && source is not string)
            {
                var list = new List<Union>();
                foreach (var item in unions)
                    list.Add(ToUnion(item));
                value = (T)(object)list;
                return true;
            }

            if (Info<T>.IsConvertible && source is IConvertible)
            {
                try
                {
                    value = (T)Convert.ChangeType(source, typeof(T));
                    return true;
                }
                catch (Exception)
                {
                    // Not convertible after all; fall through.
                }
            }

        Fail:
            value = default;
            return false;
        }

        private static bool TryNumber(object source, out double number)
        {
            switch (source)
            {
                case float f: number = f; return true;
                case int i: number = i; return true;
                case double d: number = d; return true;
                case bool b: number = b ? 1d : 0d; return true;
                case long l: number = l; return true;
                case short s: number = s; return true;
                case byte by: number = by; return true;
                case uint ui: number = ui; return true;
                case IntTag tag: number = tag.Value; return true;
                case LayerMask mask: number = mask.value; return true;
                case Enum e: number = Convert.ToInt64(e); return true;
                default: number = 0d; return false;
            }
        }
    }
}
