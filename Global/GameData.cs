using Remedy.Framework;
/*using SaintsField;
using SaintsField.Playa;*/
using System;
using System.Collections.Generic;
using UnityEngine;
//using SaintsField.ComponentHeader;

// TODO: Add compiler Ifs for Module Assemblies to dynamically add Modifiable Data
using Remedy.Animation;
using Remedy.Audio;
using Remedy.CharacterControllers.Hover;
using Remedy.CharacterControllers.LedgeGrab;
using Remedy.CharacterControllers.Stamina;
using Remedy.CharacterControllers.WallSlide;
using Remedy.CharacterControllers;
using Remedy.Damagables;
using Remedy.Inventories;
using Remedy.Weapons.Projectiles;
using Remedy.Weapons;
using Remedy.Schematics;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Remedy.Common
{

    //[Searchable]
    [CreateAssetMenu(menuName = "Remedy Toolkit/Universal Game Data")]
    public class GameData : SingletonData<GameData>
    {
        /// <summary>
        /// Searches through the Assets folder for all Components to display in Game Data.
        /// </summary>
        //[HeaderButton(label: "🔃 Reload Assets", tooltip: "Searches through the Assets folder for all Components to display in Game Data.")]
        public void ReloadAssets()
        {
#if UNITY_EDITOR
            Input = RemedyInput.Instance;

            Damageables = AssetLoaderUtility.LoadAllBehaviorsInPrefabs<Damageable>();

            Weapons = AssetLoaderUtility.LoadAllBehaviorsInPrefabs<Weapon>();

            Projectiles = AssetLoaderUtility.LoadAllBehaviorsInPrefabs<Projectile>();

            Inventories = AssetLoaderUtility.LoadAllBehaviorsInPrefabs<Inventory>();

            CollectableInventories = AssetLoaderUtility.LoadAllBehaviorsInPrefabs<CollectableInventory>();


            SoundCues = AssetLoaderUtility.LoadAllBehaviorsInPrefabs<SoundCue>();

            if (PlayerPrefab != null && PlayerPrefab.TryGetComponent(out SchematicObject player))
                Components.ReadFrom(player);

            Triggers = LevelDesign.LevelDesignData.Instance.Triggers;
            DamageData = Damagables.DamageData.Instance;
#endif
        }

/*        [Layout("Game", ELayout.Tab | ELayout.Collapse)]
        [Layout("Game/Global", ELayout.Tab | ELayout.Tab)]
        [InfoBox("A script that manages player control and allows Scriptable Events to be paired with device Inputs.")]
        [Expandable]*/
        public RemedyInput Input;

        // --- UI --- //
/*        [Layout("Game", ELayout.Tab | ELayout.Collapse)]
        [Layout("Game/UI", ELayout.Tab | ELayout.Tab)]
        [LayoutStart("./Prefabs")]
        [InfoBox("The Master Canvas is instantiated when any Canvas Based UI is instantiated. It's script is used to manage sorting of 'layers'.")]
        [Expandable]*/
        public Canvas MasterCanvas;

        // --- Levels --- //
/*        [Layout("Game", ELayout.Tab | ELayout.Collapse)]
        [Layout("Game/Levels", ELayout.Tab | ELayout.Tab)]
        [LayoutStart("./Properties")]*/
        public LevelDesign.LevelDesignData.TriggerInfo Triggers;

        // --- Player Input --- //
        /*[Layout("Game", ELayout.Tab | ELayout.Collapse)]
        [Layout("Game/Player", ELayout.Tab | ELayout.Tab)]
        [LayoutStart("./Components")]
        [Tag]*/
        public string PlayerTag;

        // --- Character Controller --- //
        /*[Layout("Game", ELayout.Tab | ELayout.Collapse)]
        [Layout("Game/Character Controller", ELayout.Tab | ELayout.Tab)]
        [LayoutStart("./Components")]
        [InfoBox("Add the Player Prefab here to display it's Components in Game Data.", show: "HasPlayerPrefab")]*/
        public GameObject PlayerPrefab;
        public bool HasPlayerPrefab => PlayerPrefab != null;

//        [ShowIf("HasPlayerPrefab")]
        [Tooltip("All the Components attached to the Player Prefab.")]
        public PlayerComponents Components;

        /// <summary>
        /// The tuning data the Player Prefab's Behaviors are set up with. Filled in by Reload Assets; editing an asset here
        /// edits it for every object that uses it.
        /// </summary>
        [Serializable]
        public class PlayerComponents
        {
            [Header("State Components")]
            [Tooltip("The Player's State Machine (open it from the State Machine Behavior, or double-click the asset).")]
            public StateMachineDefinition StateMachine;
            [Tooltip("The Stamina Controller can be used accross the board by any other Behavior, but is attached to the Player Prefab.")]
            public StaminaProperties StaminaProperties;
            [Tooltip("Parameter management through Signals, for easily handling animation in-editor.")]
            public AnimationComunicatorProperties AnimationCommunicator;

            [Header("State Based Components")]
            [Tooltip("The Default Character Controller's tuning values.")]
            public CharacterControllerProperties DefaultMotionProperties;
            public CharacterMotionProperties MotionProperties;
            public CharacterRaycastProperties RaycastProperties;
            public HoverControllerProperties HoverProperties;
            public LedgeGrabControllerProperties LedgeGrabProperties;
            public WallSlideControllerProperties WallSlideProperties;

            [Header("Audio")]
            public List<PrefabBehavior> SoundCues = new();

            /// <summary>Reads the Properties assets the Player Prefab's Behaviors are configured with.</summary>
            public void ReadFrom(SchematicObject player)
            {
                StateMachine = PrefabBehavior.ReadDefault<global::StateMachine, StateMachineDefinition>(player, "Definition") ?? StateMachine;
                StaminaProperties = PrefabBehavior.ReadDefault<StaminaController, StaminaProperties>(player, "Properties") ?? StaminaProperties;
                AnimationCommunicator = PrefabBehavior.ReadDefault<AnimationComunicator, AnimationComunicatorProperties>(player, "Properties") ?? AnimationCommunicator;
                DefaultMotionProperties = PrefabBehavior.ReadDefault<PhysicsBasedCharacterController, CharacterControllerProperties>(player, "Properties") ?? DefaultMotionProperties;
                MotionProperties = PrefabBehavior.ReadDefault<CharacterMotionContext, CharacterMotionProperties>(player, "Properties") ?? MotionProperties;
                RaycastProperties = PrefabBehavior.ReadDefault<CharacterRaycastContext, CharacterRaycastProperties>(player, "Properties") ?? RaycastProperties;
                HoverProperties = PrefabBehavior.ReadDefault<HoverController, HoverControllerProperties>(player, "Properties") ?? HoverProperties;
                LedgeGrabProperties = PrefabBehavior.ReadDefault<LedgeGrabController, LedgeGrabControllerProperties>(player, "Properties") ?? LedgeGrabProperties;
                WallSlideProperties = PrefabBehavior.ReadDefault<WallSlideController, WallSlideControllerProperties>(player, "Properties") ?? WallSlideProperties;

                SoundCues = PrefabBehavior.Find<SoundCue>(player);
            }
        }

        // --- Damageables --- //
/*        [Layout("Game", ELayout.Tab | ELayout.Collapse)]
        [Layout("Game/Weapons&Damage", ELayout.Tab | ELayout.Tab)]
        [LayoutStart("./Properties")]*/
        public DamageData DamageData;
/*        [Layout("Game", ELayout.Tab | ELayout.Collapse)]
        [Layout("Game/Weapons&Damage", ELayout.Tab | ELayout.Tab)]
        [LayoutStart("./Prefabs")]*/
        public List<PrefabBehavior> Damageables;

        // --- Weapons --- //
/*        [Layout("Game", ELayout.Tab | ELayout.Collapse)]
        [Layout("Game/Weapons&Damage", ELayout.Tab | ELayout.Tab)]
        [LayoutStart("./Prefabs")]*/
        public List<PrefabBehavior> Weapons;
        public List<PrefabBehavior> Projectiles;

        // --- Inventory --- //
/*        [Layout("Game", ELayout.Tab | ELayout.Collapse)]
        [Layout("Game/Inventory", ELayout.Tab | ELayout.Tab)]
        [LayoutStart("./Properties")]*/
        public float ItemPickupRange = 2f;
/*        [Layout("Game", ELayout.Tab | ELayout.Collapse)]
        [Layout("Game/Inventory", ELayout.Tab | ELayout.Tab)]
        [LayoutStart("./Prefabs")]*/
        public List<PrefabBehavior> Inventories;
        //[InfoBox("Collectable Inventories are Inventory Items, but can be one or more inventory items at once.")]
        public List<PrefabBehavior> CollectableInventories;

        // --- Audio --- //
        /*[Layout("Game", ELayout.Tab | ELayout.Collapse)]
        [Layout("Game/Audio", ELayout.Tab | ELayout.Collapse)]
        [LayoutStart("./Data")]
        [Expandable]*/
        public AudioData AudioData;
        /*[Layout("Game", ELayout.Tab | ELayout.Collapse)]
        [Layout("Game/Audio", ELayout.Tab | ELayout.Tab)]
        [LayoutStart("./Prefabs")]*/
        public List<PrefabBehavior> SoundCues;

        // --- Editor --- //

/*        [Layout("Editor", ELayout.Tab | ELayout.Collapse)]
        [LayoutStart("./Gizmos")]*/
        public List<string> gizmoClassNames;

        // ---- Dropdown Lists ---- //


        /// <summary>
        /// A prefab, and the GameObject in it, that a Schematic Behavior is attached to.
        /// </summary>
        [Serializable]
        public class PrefabBehavior
        {
            [Tooltip("The Prefab containing this Behavior.")]
            public SchematicObject Prefab;
            [Tooltip("The GameObject in the Prefab the Behavior is attached to.")]
            public GameObject Target;
            public string Behavior;

            public PrefabBehavior(SchematicObject prefab, GameObject target, Type behavior)
            {
                Prefab = prefab;
                Target = target;
                Behavior = behavior.Name;
            }

            /// <summary>Every GameObject of a Schematic Object that a Behavior of type T is attached to.</summary>
            public static List<PrefabBehavior> Find<T>(SchematicObject prefab) where T : SchematicBehavior
            {
                var result = new List<PrefabBehavior>();
                if (prefab == null || prefab.Behaviors == null)
                    return result;

                foreach (var entry in prefab.Behaviors)
                {
                    if (entry.Key == null || entry.Value?.Items == null) continue;
                    foreach (var attachment in entry.Value.Items)
                        if (attachment?.Metadata != null && typeof(T).IsAssignableFrom(attachment.Metadata.BehaviorType.Type))
                            result.Add(new PrefabBehavior(prefab, entry.Key, attachment.Metadata.BehaviorType.Type));
                }
                return result;
            }

            /// <summary>
            /// The value a prefab's Behavior is set up with for a Variable: its override, else the Metadata default.
            /// Null if the Behavior isn't attached or the value isn't set.
            /// </summary>
            public static TValue ReadDefault<TBehavior, TValue>(SchematicObject prefab, string variable)
                where TBehavior : SchematicBehavior where TValue : class
            {
                var attachment = prefab != null ? prefab.FindAttachment(typeof(TBehavior)) : null;
                if (attachment == null)
                    return null;

                var entry = attachment.FindOverride(variable);
                if (entry?.Value != null && entry.Value.Get<TValue>() is TValue value)
                    return value;

                var definition = attachment.Metadata != null ? attachment.Metadata.FindVariable(variable) : null;
                return definition?.Default != null ? definition.Default.Get<TValue>() : null;
            }
        }

    }

#if UNITY_EDITOR
    public static class AssetLoaderUtility
    {
        public static List<T> LoadAllAssetsOfType<T>() where T : UnityEngine.Object
        {
            string[] guids = AssetDatabase.FindAssets($"t:{typeof(T).Name}");
            List<T> assets = new List<T>();

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                T asset = AssetDatabase.LoadAssetAtPath<T>(path);
                if (asset != null)
                {
                    assets.Add(asset);
                }
            }

            return assets;
        }

        /// <summary>
        /// Every place a Schematic Behavior of type T is attached in the project's prefabs.
        /// </summary>
        public static List<GameData.PrefabBehavior> LoadAllBehaviorsInPrefabs<T>() where T : SchematicBehavior
        {
            var results = new List<GameData.PrefabBehavior>();
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab"))
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                if (prefab != null && prefab.TryGetComponent(out SchematicObject schematic))
                    results.AddRange(GameData.PrefabBehavior.Find<T>(schematic));
            }
            return results;
        }

        public static List<T> LoadAllComponentsInPrefabs<T>() where T : Component
        {
            // Find all prefab asset GUIDs
            string[] guids = AssetDatabase.FindAssets("t:Prefab");
            List<T> components = new List<T>();

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);

                if (prefab != null)
                {
                    // Find all components of type T in the prefab (including children)
                    T[] found = prefab.GetComponentsInChildren<T>(true);
                    components.AddRange(found);
                }
            }

            return components;
        }

    }
#endif
}