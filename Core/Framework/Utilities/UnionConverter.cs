using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Remedy.Framework.Utils
{
    /// <summary>
    /// Per-type cached converter from an arbitrary generic value T into a Union.
    /// The static constructor (and the typeof(T) == ... chain inside BuildConverter)
    /// runs exactly once per distinct closed generic type T - after that, ToUnion(ref value)
    /// is just a direct delegate call. Value types go through Union.UnsafeCastAs to avoid
    /// boxing entirely; reference types are just referenced, not boxed, so a plain cast is fine there.
    /// </summary>
    public static class UnionConverter<T>
    {
        public delegate Union ConverterDelegate(ref T value);

        public static readonly ConverterDelegate ToUnion = BuildConverter();

        private static ConverterDelegate BuildConverter()
        {
            var t = typeof(T);

            // ---- Value types: unsafe cast, zero boxing ----
            if (t == typeof(bool))
                return (ref T v) => new Union(Union.UnsafeCastAs<T, bool>(ref v));
            if (t == typeof(int))
                return (ref T v) => new Union(Union.UnsafeCastAs<T, int>(ref v));
            if (t == typeof(float))
                return (ref T v) => new Union(Union.UnsafeCastAs<T, float>(ref v));
            if (t == typeof(Vector2))
                return (ref T v) => new Union(Union.UnsafeCastAs<T, Vector2>(ref v));
            if (t == typeof(Vector3))
                return (ref T v) => new Union(Union.UnsafeCastAs<T, Vector3>(ref v));
            if (t == typeof(Vector4))
                return (ref T v) => new Union(Union.UnsafeCastAs<T, Vector4>(ref v));
            if (t == typeof(Color))
                return (ref T v) => new Union(Union.UnsafeCastAs<T, Color>(ref v));
            if (t == typeof(Color32))
                return (ref T v) => new Union(Union.UnsafeCastAs<T, Color32>(ref v));
            if (t == typeof(Quaternion))
                return (ref T v) => new Union(Union.UnsafeCastAs<T, Quaternion>(ref v));
            if (t == typeof(LayerMask))
                return (ref T v) => new Union(Union.UnsafeCastAs<T, LayerMask>(ref v));
            if (t == typeof(Scene))
                return (ref T v) => new Union(Union.UnsafeCastAs<T, Scene>(ref v));

            // ---- Reference types: plain cast, no boxing to begin with ----
            if (t == typeof(string))
                return (ref T v) => new Union((string)(object)v);
            if (t == typeof(GameObject))
                return (ref T v) => new Union((GameObject)(object)v);
            if (t == typeof(PhysicsMaterial))
                return (ref T v) => new Union((PhysicsMaterial)(object)v);
            if (t == typeof(List<Union>))
                return (ref T v) => new Union((List<Union>)(object)v);
            if (t == typeof(Dictionary<string, Union>))
                return (ref T v) => new Union((Dictionary<string, Union>)(object)v);
            if (t == typeof(Union))
                return (ref T v) => (Union)(object)v;

            // Component and any of its subclasses (Transform, Rigidbody, Renderer, etc.)
            if (typeof(Component).IsAssignableFrom(t))
                return (ref T v) => new Union((Component)(object)v);

            // Arrays of any element type
            if (typeof(Array).IsAssignableFrom(t))
                return (ref T v) => new Union((Array)(object)v);

            // ---- No matching Union constructor exists for these yet ----
            // Material, Texture, AudioClip, and plain UnityEngine.Object are declared in
            // Union.ValueType / TypeLookup but the Union struct itself has no public
            // constructor for them. Rather than silently mis-tag them, this falls through
            // to Union.Null so the gap is visible instead of hidden. Add constructors to
            // Union for these (mirroring the GameObject/Component ones) if you need them,
            // then add matching branches here.
            return (ref T v) =>
            {
                Debug.LogWarning($"UnionConverter<{t.Name}>: no Union conversion defined for this type; returning Union.Null.");
                return Union.Null;
            };
        }
    }
}