using Remedy.Framework;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Remedy.Surfaces
{
    /// <summary>
    /// What a surface is made of, for anything that asks (footsteps, sound occlusion, sliding, impacts...): a few
    /// common properties and any of your own, by name. Painted onto level geometry in the Level Designer (Variant
    /// mode, by its Paint Color), given to whole objects with a <see cref="SurfaceMaterialTag"/>, or used as the
    /// project's default for everything else (<see cref="SurfaceLibrary"/>).
    /// </summary>
    [CreateAssetMenu(fileName = "New Surface Material", menuName = "Remedy Toolkit/Surface Material", order = 200)]
    public class SurfaceMaterial : ScriptableObject
    {
        [Tooltip("Its color when painting surfaces, and in the paint overlay.")]
        public Color PaintColor = new(0.6f, 0.6f, 0.6f, 1f);

        [Header("Properties")]
        [Range(0f, 1f), Tooltip("How wet it is: 0 dry, 1 standing water (splashier footsteps, slipping...).")]
        public float Wetness;
        [Range(0f, 1f), Tooltip("How much sound it soaks up: 0 bare concrete or metal, 1 thick carpet or foam (muffles sound passing through or bouncing off it).")]
        public float Absorption = 0.3f;
        [Range(0f, 1f), Tooltip("How hard it is: 0 soft (mud, snow), 1 hard (stone, metal).")]
        public float Hardness = 0.5f;
        [Range(0f, 1f), Tooltip("How grippy it is: 0 ice, 1 rubber.")]
        public float Friction = 0.6f;

        [Tooltip("Your own properties, by name (Get Surface Property reads these too).")]
        public List<Property> Custom = new();

        [Serializable]
        public struct Property
        {
            public string Name;
            public float Value;
        }

        /// <summary>The built-in properties' names, for menus.</summary>
        public static readonly string[] BuiltInProperties = { "Wetness", "Absorption", "Hardness", "Friction" };

        /// <summary>A property by name (built-in or custom; case doesn't matter). False if it has none by that name.</summary>
        public bool TryGet(string property, out float value)
        {
            switch ((property ?? "").Trim().ToLowerInvariant())
            {
                case "wetness": value = Wetness; return true;
                case "absorption": value = Absorption; return true;
                case "hardness": value = Hardness; return true;
                case "friction": value = Friction; return true;
            }
            if (Custom != null)
                foreach (var custom in Custom)
                    if (string.Equals(custom.Name?.Trim(), property?.Trim(), StringComparison.OrdinalIgnoreCase))
                    {
                        value = custom.Value;
                        return true;
                    }
            value = 0f;
            return false;
        }

        /// <summary>A property by name, or <paramref name="fallback"/> when it has none by that name.</summary>
        public float Get(string property, float fallback = 0f) => TryGet(property, out float value) ? value : fallback;
    }

    /// <summary>
    /// The project's Surface Materials: the Default, which everything not painted or tagged is made of, and the list
    /// the paint brush offers. Kept in Resources (made the first time it's needed in the editor).
    /// </summary>
    public class SurfaceLibrary : SingletonData<SurfaceLibrary>
    {
        [Tooltip("What untagged, unpainted surfaces are made of.")]
        public SurfaceMaterial Default;
        [Tooltip("The materials the paint brush offers (Surface Materials in the project are added automatically).")]
        public List<SurfaceMaterial> Materials = new();

        private static SurfaceLibrary _found;
        private static bool _looked;

        /// <summary>The library, looked up once (null in a build without one).</summary>
        public static SurfaceLibrary Current
        {
            get
            {
                if (!_looked || (_found == null && Application.isEditor && !Application.isPlaying))
                {
                    try { _found = Instance; }
                    catch { _found = null; }
                    _looked = true;
                }
                return _found;
            }
        }

        /// <summary>The default material, or null when there's no library or default.</summary>
        public static SurfaceMaterial DefaultMaterial => Current != null ? Current.Default : null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void NewSession()
        {
            _found = null;
            _looked = false;
        }
    }

    /// <summary>
    /// Makes a whole object (and its children) one Surface Material, wherever it isn't painted: a metal crate, a
    /// wooden bridge, a carpeted room's floor.
    /// </summary>
    [AddComponentMenu("Remedy/Surfaces/Surface Material Tag")]
    public class SurfaceMaterialTag : MonoBehaviour
    {
        public SurfaceMaterial Material;

        /// <summary>The material an object is tagged with (by it or a parent), or null.</summary>
        public static SurfaceMaterial Of(GameObject target)
        {
            if (target == null)
                return null;
            var tag = target.GetComponentInParent<SurfaceMaterialTag>();
            return tag != null ? tag.Material : null;
        }
    }
}
