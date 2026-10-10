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
using Remedy.StateMachines;
using Remedy.Weapons.Projectiles;
using Remedy.Weapons;

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

            Damageables.Clear();
            var damageables = AssetLoaderUtility.LoadAllComponentsInPrefabs<Damageable>();
            foreach (var damageable in damageables)
            {
                Damageables.Add(new(damageable));
            }

            Weapons.Clear();
            var weapons = AssetLoaderUtility.LoadAllComponentsInPrefabs<Weapon>();
            foreach (var weapon in weapons)
            {
                Weapons.Add(new(weapon));
            }

            Projectiles.Clear();
            var projectiles = AssetLoaderUtility.LoadAllComponentsInPrefabs<Projectile>();
            foreach (var projectile in projectiles)
            {
                Projectiles.Add(new(projectile));
            }

            Inventories.Clear();
            var inventories = AssetLoaderUtility.LoadAllComponentsInPrefabs<Inventory>();
            foreach (var inventory in inventories)
            {
                Inventories.Add(new(inventory));
            }

            CollectableInventories.Clear();
            var collectableInventories = AssetLoaderUtility.LoadAllComponentsInPrefabs<CollectableInventory>();
            foreach (var collectableInventory in collectableInventories)
            {
                CollectableInventories.Add(new(collectableInventory));
            }


            SoundCues.Clear();
            var soundCues = AssetLoaderUtility.LoadAllComponentsInPrefabs<SoundCue>();
            foreach (var soundCue in soundCues)
            {
                SoundCues.Add(new(soundCue));
            }

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

        [Serializable]
        public class PlayerComponents
        {
            [Header("State Components")]
            /*[InfoBox("Components that control state and how it's displayed.")]
            [Expandable]*/
            public MBStateMachine StateMachine;
            [Tooltip("The Stamina Controller can be used accross the board by any other Component, but is attached to the Player Prefab.")]
            //[Expandable]
            public StaminaController StaminaController;
            [Tooltip("This is the Component that controls the Transformation of the Character's Mesh in accordance to the state and motion of the Character Controller.")]
            //[Expandable]
            public CharacterControllerRig CharacterControllerRig;
            [Tooltip("Subscribes Parameter management to Scriptable Events for easily handling animation in-editor.")]
            public AnimationComunicator AnimationCommunicator;

            [Header("State Based Components")]
            /*[InfoBox("These components are enabled/disabled by the MB State Machine. These contain the functionality & properties of the Character Controller.")]
            [Expandable]*/
            [Tooltip("The Default Character Controller is a Schematic Behavior, so its tuning values are shown here instead of a component.")]
            public CharacterControllerProperties DefaultMotionProperties;
            //[Expandable]
            public HoverController HoverController;
            //[Expandable]
            public LedgeGrabController LedgeGrabController;
            //[Expandable]
            public WallSlideController WallSlideController;

            [Header("Weapons & Damage")]
            public WeaponHolder WeaponHolder;
            public Damageable Damageable;

            [Header("Audio")]
            public List<ExpandablePrefabComponent<SoundCue>> SoundCues;
        }

        // --- Damageables --- //
/*        [Layout("Game", ELayout.Tab | ELayout.Collapse)]
        [Layout("Game/Weapons&Damage", ELayout.Tab | ELayout.Tab)]
        [LayoutStart("./Properties")]*/
        public DamageData DamageData;
/*        [Layout("Game", ELayout.Tab | ELayout.Collapse)]
        [Layout("Game/Weapons&Damage", ELayout.Tab | ELayout.Tab)]
        [LayoutStart("./Prefabs")]*/
        public List<ExpandablePrefabComponent<Damageable>> Damageables;

        // --- Weapons --- //
/*        [Layout("Game", ELayout.Tab | ELayout.Collapse)]
        [Layout("Game/Weapons&Damage", ELayout.Tab | ELayout.Tab)]
        [LayoutStart("./Prefabs")]*/
        public List<ExpandablePrefabComponent<Weapon>> Weapons;
        public List<ExpandablePrefabComponent<Projectile>> Projectiles;

        // --- Inventory --- //
/*        [Layout("Game", ELayout.Tab | ELayout.Collapse)]
        [Layout("Game/Inventory", ELayout.Tab | ELayout.Tab)]
        [LayoutStart("./Properties")]*/
        public float ItemPickupRange = 2f;
/*        [Layout("Game", ELayout.Tab | ELayout.Collapse)]
        [Layout("Game/Inventory", ELayout.Tab | ELayout.Tab)]
        [LayoutStart("./Prefabs")]*/
        public List<ExpandablePrefabComponent<Inventory>> Inventories;
        //[InfoBox("Collectable Inventories are Inventory Items, but can be one or more inventory items at once.")]
        public List<ExpandablePrefabComponent<CollectableInventory>> CollectableInventories;

        // --- Audio --- //
        /*[Layout("Game", ELayout.Tab | ELayout.Collapse)]
        [Layout("Game/Audio", ELayout.Tab | ELayout.Collapse)]
        [LayoutStart("./Data")]
        [Expandable]*/
        public AudioData AudioData;
        /*[Layout("Game", ELayout.Tab | ELayout.Collapse)]
        [Layout("Game/Audio", ELayout.Tab | ELayout.Tab)]
        [LayoutStart("./Prefabs")]*/
        public List<ExpandablePrefabComponent<SoundCue>> SoundCues;

        // --- Editor --- //

/*        [Layout("Editor", ELayout.Tab | ELayout.Collapse)]
        [LayoutStart("./Gizmos")]*/
        public List<string> gizmoClassNames;

        // ---- Dropdown Lists ---- //


        [Serializable]
        public class ExpandablePrefabComponent<T> where T : MonoBehaviour
        {
            [Tooltip("The Prefab containing this Component.")]
            //[AssetPreview(50)]
            //[ReadOnly]
            public GameObject Prefab;
            //[Expandable]
            public T Component;

            public ExpandablePrefabComponent(T component)
            {
                Component = component;
                Prefab = component.gameObject;
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