using System;
using System.Collections.Generic;

namespace Remedy.Schematics.Assistant
{
    /// <summary>
    /// What Sprocket says about Unity's own components and assets (the toolkit's pieces describe themselves with
    /// [SchematicDoc]). By type name; the first match up the type's inheritance wins.
    /// </summary>
    internal static class UnityObjectDocs
    {
        public static readonly Dictionary<string, string> Components = new(StringComparer.Ordinal)
        {
            ["Transform"] = "Where the object is: its position, rotation and scale, and its place in the hierarchy.",
            ["RectTransform"] = "Where a UI element is: its anchors, size and position on the screen.",
            ["Camera"] = "Renders the scene from this point of view. The game shows what the main camera sees.",
            ["Light"] = "Lights the scene: directional (like the sun), point (a bulb), spot (a flashlight) or area.",
            ["MeshFilter"] = "Holds the 3D shape (mesh) the object's Mesh Renderer draws.",
            ["MeshRenderer"] = "Draws the object's 3D shape with its materials.",
            ["SkinnedMeshRenderer"] = "Draws a 3D model that bends with a skeleton, for animated characters.",
            ["SpriteRenderer"] = "Draws a 2D image (sprite) in the world.",
            ["LineRenderer"] = "Draws a line through a list of points: lasers, ropes, trails.",
            ["TrailRenderer"] = "Draws a fading trail behind the object as it moves.",
            ["ParticleSystem"] = "Spawns lots of small particles: fire, smoke, sparks, magic.",
            ["Animator"] = "Plays the object's animations, switching between them with an Animator Controller.",
            ["Animation"] = "Plays legacy animation clips on the object.",
            ["AudioSource"] = "Plays sounds from this object's position.",
            ["AudioListener"] = "The game's ears: sounds are heard from here (usually on the camera).",
            ["Rigidbody"] = "Unity physics for the object. (This toolkit's physics uses Box3D bodies instead.)",
            ["Rigidbody2D"] = "Unity 2D physics for the object.",
            ["Collider"] = "A Unity physics shape for collisions and triggers.",
            ["BoxCollider"] = "A box-shaped Unity collider.",
            ["SphereCollider"] = "A sphere-shaped Unity collider.",
            ["CapsuleCollider"] = "A capsule-shaped Unity collider, common for characters.",
            ["MeshCollider"] = "A Unity collider shaped like a mesh, often for level geometry.",
            ["CharacterController"] = "Unity's built-in capsule character mover.",
            ["NavMeshAgent"] = "Walks the object around the level along the navigation mesh, for AI pathfinding.",
            ["Terrain"] = "A large landscape: hills, textures, trees and grass.",
            ["TerrainCollider"] = "Makes a Terrain solid.",
            ["Canvas"] = "The surface UI is drawn on: screen overlay, camera-facing, or in the world.",
            ["CanvasScaler"] = "Keeps UI the right size across screen resolutions.",
            ["GraphicRaycaster"] = "Lets UI on this Canvas be clicked.",
            ["Image"] = "A UI image.",
            ["RawImage"] = "A UI texture.",
            ["Text"] = "UI text.",
            ["TextMeshProUGUI"] = "Crisp UI text (TextMeshPro).",
            ["TextMeshPro"] = "Crisp text in the 3D world (TextMeshPro).",
            ["Button"] = "A clickable UI button.",
            ["Slider"] = "A UI slider for picking a value.",
            ["Toggle"] = "A UI checkbox.",
            ["EventSystem"] = "Sends input to the UI. One per scene.",
            ["ReflectionProbe"] = "Captures the surroundings so shiny surfaces nearby reflect them.",
            ["LightProbeGroup"] = "Samples light at points so moving objects are lit like their surroundings.",
            ["Volume"] = "Post-processing and rendering settings (bloom, color, fog) for the camera inside or for the whole scene.",
            ["PlayableDirector"] = "Plays a Timeline: cutscenes and scripted sequences.",
            ["VideoPlayer"] = "Plays a video on a surface, the camera or a texture.",
            ["Tilemap"] = "A 2D grid of tiles for 2D levels.",
            ["Grid"] = "The grid tilemaps are laid out on.",
            ["NetworkObject"] = "Makes the object exist across the network (Netcode for GameObjects).",
            ["SchematicObject"] = "Makes this a Schematic Object: its Schematic Graph and Behaviors run on it.",
            ["Box3DBody"] = "The object's Box3D physics body: how it moves, falls and collides.",
        };

        public static readonly Dictionary<string, string> Assets = new(StringComparer.Ordinal)
        {
            ["GameObject"] = "A prefab: a saved object you can place in scenes or spawn while playing.",
            ["SceneAsset"] = "A scene: a level or screen, with everything placed in it.",
            ["Material"] = "A material: how a surface looks (its shader, colors and textures).",
            ["Shader"] = "A shader: the program that draws surfaces.",
            ["Texture2D"] = "A texture: an image used on materials, sprites or UI.",
            ["Sprite"] = "A sprite: a 2D image for sprites and UI.",
            ["Mesh"] = "A mesh: a 3D shape.",
            ["AnimationClip"] = "An animation clip: one motion, like a run or a jump.",
            ["AnimatorController"] = "An Animator Controller: the states and transitions between a character's animations.",
            ["AudioClip"] = "A sound or piece of music.",
            ["AudioMixer"] = "An audio mixer: groups and effects for the game's sound.",
            ["Font"] = "A font for text.",
            ["MonoScript"] = "A C# script.",
            ["TextAsset"] = "A text or data file.",
            ["PhysicsMaterial"] = "A physics material: friction and bounciness.",
            ["RenderTexture"] = "A texture a camera draws into (screens, minimaps).",
            ["TerrainData"] = "A terrain's heights, textures and details.",
            ["DefaultAsset"] = "A folder or file Unity doesn't have a special type for.",
            ["SchematicGraph"] = "A Schematic Graph: the logic of a Schematic Object, made of nodes. Double-click to open it.",
            ["SignalData"] = "A Signal: a message Behaviors and graphs send to each other, with optional values.",
            ["BehaviorMetadata"] = "A Behavior's metadata: its Variables, Signals and default values, generated from its code.",
            ["StateMachineDefinition"] = "A State Machine: states, the Behaviors each one enables, and the transitions between them.",
            ["BrainDefinition"] = "A Brain Definition: a learning AI's inputs and outputs, how it learns, and what it has learned.",
            ["MnnBrain"] = "Saved learning for a Manifold Network brain: its weights.",
            ["MnnConfig"] = "Settings for a Manifold Neuron Network.",
            ["ScriptableObject"] = "A data asset.",
        };

        /// <summary>The description for a type, looking up its inheritance (e.g. a custom Collider finds Collider).</summary>
        public static string For(Type type, Dictionary<string, string> table)
        {
            for (var t = type; t != null; t = t.BaseType)
                if (table.TryGetValue(t.Name, out var text))
                    return text;
            return null;
        }
    }
}
