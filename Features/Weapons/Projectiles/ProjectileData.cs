// project armada

#pragma warning disable 0414

/*using SaintsField;
using SaintsField.Playa;*/
using Remedy.Framework;
using UnityEngine;
using UnityEngine.Serialization;

namespace Remedy.Weapons.Projectiles
{
    //[Searchable]
    [CreateAssetMenu(menuName = "Remedy Toolkit/Weapons/Projectile Data")]
    public class ProjectileData : ScriptableObjectWithID<ProjectileData>
    {
        public enum TargetMode
        {
            MatchingTag,
            SpecificInstance
        }
        /*[Layout("Settings", ELayout.Tab | ELayout.Collapse)]
        [LayoutStart("./Targetting")]*/
        public TargetMode TargetingMode;
        [Tooltip("The Int Tag of the objects this Projectile targets (with Targeting Mode set to Matching Tag).")]
        [SerializeField]
        private IntTag _targetTag = IntTags.Untagged;

        // Older assets saved a Unity tag name here; it's moved onto the Int Tag of the same name the first time it's read.
        [SerializeField, HideInInspector, FormerlySerializedAs("TargetTag")]
        private string _legacyTargetTag;

        /// <summary>The Int Tag of the objects this Projectile targets (with <see cref="TargetMode.MatchingTag"/>).</summary>
        public IntTag TargetTag
        {
            get
            {
                if (!string.IsNullOrEmpty(_legacyTargetTag))
                {
                    if (IntTags.TryGetId(_legacyTargetTag, out int id))
                        _targetTag = id;
                    else
                        Debug.LogWarning($"[{name}] The target tag \"{_legacyTargetTag}\" has no Int Tag with that name yet. Add one and pick it in Target Tag.", this);
                    _legacyTargetTag = null;
                }
                return _targetTag;
            }
            set => _targetTag = value;
        }

        [Tooltip("The Distance away from the original shoot position where the projectile lines up completely with the actual aim position.")]
        public float LineUpDistance = 5f;
        /*[Layout("Settings", ELayout.Tab | ELayout.Collapse)]
        [LayoutStart("./Speed")]*/
        public float InitialVelocity;
        [Range(0f, 1f)]
        public float GravityInfluence;


/*        [Layout("Settings", ELayout.Tab | ELayout.Collapse)]
        [LayoutStart("./Life & Cost")]*/
        public float LifeSpan = 5f;
        [Tooltip("Whether to treat any collision as a valid collision for the Projectile")]
        public bool DestroyOnContactWithAny;
        [Tooltip("The amount of Ammo that is spent to create this projectile")]
        public int AmmoCost;


        [SerializeField]
        [Tooltip("If True, a FixedJoint will be added to the Projectile, and it will be attached to the Weapon that fires it.")]
        private bool _fixToWeapon = false;
        public bool Fixed => _fixToWeapon;

        [Tooltip("The LayerMask to use for Targetting Enemies")]
        //[Layer]
        public int Layer;
        public LayerMask CollisionLayer;
    }
}