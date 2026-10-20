using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Remedy.LevelDesigner.Editor
{
    /// <summary>
    /// Composes a level: its blockout and some of its variants merged into one scene. Used two ways:
    /// <list type="bullet">
    /// <item><b>Builds</b> (<see cref="LevelBuildComposition"/>): for every blockout in the build whose level has
    /// variants flagged "Compose into build", a composed scene replaces the blockout in the build's scene list, for
    /// that build only. The scene assets in the project are never changed.</item>
    /// <item><b>Test Composed Scene</b> (<see cref="TestComposed"/>): composes the picked variants and enters Play
    /// Mode; when Play Mode ends, the scenes that were open before come back and the test scene is deleted.</item>
    /// </list>
    /// How a level is composed: the first picked variant is the base (its lighting, skybox and fog are the scene's,
    /// since variants are where the art and lighting are made), then the blockout and the other variants are merged
    /// into it with <see cref="SceneManager.MergeScenes"/>. The <see cref="VariantScene"/> and
    /// <see cref="BlockoutScene"/> markers are removed from the result (the blockout is already in it, so a variant
    /// must not load it again). The blockout comes along visible, collision-only or not at all, per
    /// <see cref="BlockoutLevel.ComposedBlockout"/>.<br/>
    /// Composed scenes are written to <see cref="GeneratedFolder"/>, which is deleted again afterwards.
    /// </summary>
    [InitializeOnLoad]
    public static class LevelComposer
    {
        /// <summary>Where composed scenes are written while they're needed (deleted afterwards).</summary>
        public const string GeneratedFolder = "Assets/LevelDesign.Generated";

        private const string TestFolder = GeneratedFolder + "/Test";
        private const string PendingTestKey = "Remedy.LevelDesigner.ComposedTest.Pending";
        private const string SavedSetupKey = "Remedy.LevelDesigner.ComposedTest.Setup";

        static LevelComposer()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            // Play Mode never started (compile errors, a failed enter) or Unity reloaded in between: put things back.
            if (SessionState.GetBool(PendingTestKey, false) && !EditorApplication.isPlayingOrWillChangePlaymode)
                EditorApplication.delayCall += FinishTest;
        }

        /// <summary>True for scenes this class generated (composed copies), which other tools should leave alone.</summary>
        public static bool IsGenerated(string scenePath)
        {
            return !string.IsNullOrEmpty(scenePath) && scenePath.Replace('\\', '/').StartsWith(GeneratedFolder + "/", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>The variants of a level flagged "Compose into build" (whose scenes exist).</summary>
        public static List<Variant> FlaggedVariants(BlockoutLevel blockout)
        {
            var list = new List<Variant>();
            if (blockout == null)
                return list;
            foreach (var variant in blockout.Variants)
            {
                string path = LevelDesignerContext.PathOf(variant);
                if (variant != null && variant.ComposeIntoBuild && !string.IsNullOrEmpty(path) && File.Exists(path))
                    list.Add(variant);
            }
            return list;
        }

        // ---------------------------------------------------------------- composing

        /// <summary>
        /// Composes a level into a scene saved at <paramref name="targetPath"/> and leaves it open as the only scene.
        /// Opens scenes, so the caller saves the user's scenes first and puts their setup back afterwards (see
        /// <see cref="SaveSetup"/> / <see cref="RestoreSetup"/>). Returns false (with a logged reason) when the
        /// blockout scene can't be found.
        /// </summary>
        public static bool Compose(BlockoutLevel blockout, IList<Variant> variants, ComposedBlockoutMode blockoutMode, string targetPath)
        {
            string blockoutPath = LevelDesignerContext.PathOf(blockout);
            if (string.IsNullOrEmpty(blockoutPath) || !File.Exists(blockoutPath))
            {
                Debug.LogError($"Level Designer: can't compose \"{blockout?.Name}\": its blockout scene can't be found.");
                return false;
            }

            var variantPaths = new List<string>();
            if (variants != null)
            {
                foreach (var variant in variants)
                {
                    string path = LevelDesignerContext.PathOf(variant);
                    if (!string.IsNullOrEmpty(path) && File.Exists(path) && !variantPaths.Contains(path))
                        variantPaths.Add(path);
                    else if (variant != null)
                        Debug.LogWarning($"Level Designer: the variant \"{variant.Name}\" of \"{blockout.Name}\" can't be found; it's left out of the composed scene.");
                }
            }

            Scene composed;
            if (variantPaths.Count > 0)
            {
                // The first variant is the base: its scene settings (lighting, skybox, fog) are the composed scene's.
                composed = EditorSceneManager.OpenScene(variantPaths[0], OpenSceneMode.Single);
                var blockoutScene = EditorSceneManager.OpenScene(blockoutPath, OpenSceneMode.Additive);
                PrepareBlockout(blockoutScene, blockoutMode);
                Merge(blockoutScene, composed);
                for (int i = 1; i < variantPaths.Count; i++)
                    Merge(EditorSceneManager.OpenScene(variantPaths[i], OpenSceneMode.Additive), composed);
            }
            else
            {
                composed = EditorSceneManager.OpenScene(blockoutPath, OpenSceneMode.Single);
                PrepareBlockout(composed, blockoutMode);
            }

            SceneManager.SetActiveScene(composed);
            StripMarkers(composed);

            LevelDesignerContext.EnsureFolder(Path.GetDirectoryName(targetPath)?.Replace('\\', '/'));
            // Saved under the generated path (not over any source scene): the scene in memory now *is* the copy.
            if (!EditorSceneManager.SaveScene(composed, targetPath, false))
            {
                Debug.LogError($"Level Designer: couldn't save the composed scene to {targetPath}.");
                return false;
            }
            return true;
        }

        private static void Merge(Scene source, Scene destination)
        {
            if (!source.IsValid() || source == destination)
                return;
            SceneManager.MergeScenes(source, destination);
            // MergeScenes removes the source; make sure the editor doesn't keep an empty entry for it.
            if (source.IsValid() && source.isLoaded)
                EditorSceneManager.CloseScene(source, true);
        }

        // The blockout's part, per the level's setting. Only the in-memory copy is touched (it's never saved).
        private static void PrepareBlockout(Scene scene, ComposedBlockoutMode mode)
        {
            switch (mode)
            {
                case ComposedBlockoutMode.LeaveOut:
                    foreach (var root in scene.GetRootGameObjects())
                        UnityEngine.Object.DestroyImmediate(root);
                    break;
                case ComposedBlockoutMode.CollisionOnly:
                    foreach (var root in scene.GetRootGameObjects())
                        foreach (var shape in root.GetComponentsInChildren<BlockoutShape>(true))
                            if (shape.TryGetComponent<MeshRenderer>(out var meshRenderer))
                                meshRenderer.enabled = false;
                    break;
            }
        }

        private static void StripMarkers(Scene scene)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var variant in root.GetComponentsInChildren<VariantScene>(true))
                    UnityEngine.Object.DestroyImmediate(variant);
                foreach (var marker in root.GetComponentsInChildren<BlockoutScene>(true))
                    UnityEngine.Object.DestroyImmediate(marker);
            }
        }

        // ---------------------------------------------------------------- scene setup

        [Serializable]
        private class SavedSetup
        {
            public List<string> Paths = new();
            public List<bool> Loaded = new();
            public int Active = -1;
        }

        /// <summary>The scenes open right now (saved scenes only), to put back with <see cref="RestoreSetup"/>.</summary>
        public static SceneSetup[] SaveSetup() => EditorSceneManager.GetSceneManagerSetup();

        /// <summary>Opens the scenes of a saved setup again (skipping any that no longer exist).</summary>
        public static void RestoreSetup(SceneSetup[] setup)
        {
            var usable = new List<SceneSetup>();
            if (setup != null)
                foreach (var entry in setup)
                    if (entry != null && !string.IsNullOrEmpty(entry.path) && File.Exists(entry.path) && !IsGenerated(entry.path))
                        usable.Add(entry);

            if (usable.Count == 0)
            {
                EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
                return;
            }
            bool anyActive = usable.Exists(s => s.isActive && s.isLoaded);
            if (!anyActive)
            {
                usable[0].isLoaded = true;
                usable[0].isActive = true;
            }
            EditorSceneManager.RestoreSceneManagerSetup(usable.ToArray());
        }

        /// <summary>Deletes a generated folder (and the generated root when nothing else is left in it).</summary>
        public static void DeleteGenerated(string folder = GeneratedFolder)
        {
            if (AssetDatabase.IsValidFolder(folder))
                AssetDatabase.DeleteAsset(folder);
            if (folder != GeneratedFolder && AssetDatabase.IsValidFolder(GeneratedFolder) &&
                AssetDatabase.FindAssets("", new[] { GeneratedFolder }).Length == 0)
                AssetDatabase.DeleteAsset(GeneratedFolder);
        }

        // ---------------------------------------------------------------- Test Composed Scene

        /// <summary>
        /// Composes a level from its blockout and <paramref name="variants"/> and enters Play Mode with it. The scenes
        /// open before come back when Play Mode ends (also after a domain reload), and the test scene is deleted.
        /// Merged, not just loaded side by side, because that's exactly what a build gets: one scene, one set of
        /// lighting settings, no variant markers loading the blockout a second time.
        /// </summary>
        public static bool TestComposed(BlockoutLevel blockout, IList<Variant> variants, ComposedBlockoutMode blockoutMode)
        {
            if (blockout == null || EditorApplication.isPlayingOrWillChangePlaymode)
                return false;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return false;

            var setup = SaveSetup();
            var saved = new SavedSetup();
            for (int i = 0; i < setup.Length; i++)
            {
                saved.Paths.Add(setup[i].path);
                saved.Loaded.Add(setup[i].isLoaded);
                if (setup[i].isActive)
                    saved.Active = i;
            }

            string level = LevelDesignerContext.LevelNameOf(blockout);
            string target = $"{TestFolder}/{level}_Composed.unity";
            bool composed;
            try
            {
                composed = Compose(blockout, variants, blockoutMode, target);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                composed = false;
            }
            if (!composed)
            {
                RestoreSetup(setup);
                DeleteGenerated(TestFolder);
                return false;
            }

            SessionState.SetString(SavedSetupKey, JsonUtility.ToJson(saved));
            SessionState.SetBool(PendingTestKey, true);
            EditorApplication.EnterPlaymode();
            return true;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredEditMode && SessionState.GetBool(PendingTestKey, false))
                EditorApplication.delayCall += FinishTest;
        }

        // Back from a composed test: reopen what was open, delete the test scene.
        private static void FinishTest()
        {
            if (!SessionState.GetBool(PendingTestKey, false) || EditorApplication.isPlayingOrWillChangePlaymode)
                return;
            SessionState.EraseBool(PendingTestKey);
            var saved = JsonUtility.FromJson<SavedSetup>(SessionState.GetString(SavedSetupKey, "{}")) ?? new SavedSetup();
            SessionState.EraseString(SavedSetupKey);

            var setup = new SceneSetup[saved.Paths.Count];
            for (int i = 0; i < setup.Length; i++)
            {
                setup[i] = new SceneSetup
                {
                    path = saved.Paths[i],
                    isLoaded = i < saved.Loaded.Count && saved.Loaded[i],
                    isActive = i == saved.Active,
                };
            }
            try
            {
                RestoreSetup(setup);
            }
            finally
            {
                DeleteGenerated(TestFolder);
                LevelDesignerContext.Refresh();
            }
        }
    }

    /// <summary>
    /// Puts composed levels into builds. The build button (Build Profiles / Build Settings) goes through
    /// <see cref="BuildPlayerWindow.RegisterBuildPlayerHandler"/>: before the build, every blockout in the scene list
    /// whose level has variants flagged "Compose into build" is composed (see <see cref="LevelComposer"/>) into
    /// <c>Assets/LevelDesign.Generated/&lt;Level&gt;/&lt;blockout scene name&gt;.unity</c>, which takes the
    /// blockout's place in this build's scene list (same position, same scene name, so code loading the level by
    /// name or build index still finds it). The project's Build Profile and scenes aren't changed; the generated
    /// folder is deleted after the build, whether it worked or not.<br/>
    /// Why not swap the scene list in an IPreprocessBuildWithReport: by the time those callbacks run, the build's
    /// scene list is already fixed, and opening and merging scenes isn't allowed while a build is processing them.
    /// Scripted builds (CI) call <see cref="BuildWithComposedLevels"/> instead of BuildPipeline.BuildPlayer; a build
    /// that skipped composition gets a warning from <see cref="OnPreprocessBuild"/>.
    /// </summary>
    [InitializeOnLoad]
    public sealed class LevelBuildComposition : IPreprocessBuildWithReport
    {
        private static bool _composing;

        static LevelBuildComposition()
        {
            BuildPlayerWindow.RegisterBuildPlayerHandler(OnBuildButton);
        }

        public int callbackOrder => 0;

        private static void OnBuildButton(BuildPlayerOptions options)
        {
            BuildWithComposedLevels(options, true);
        }

        /// <summary>One blockout in the build that gets composed.</summary>
        public struct PlannedLevel
        {
            public int SceneIndex;
            public BlockoutLevel Blockout;
            public List<Variant> Variants;
        }

        /// <summary>The blockouts in <paramref name="scenes"/> that would be composed (empty when composition is off).</summary>
        public static List<PlannedLevel> Plan(string[] scenes)
        {
            var plan = new List<PlannedLevel>();
            var project = LevelDesignerContext.ExistingProject;
            if (project == null || !project.ComposeVariantsOnBuild || scenes == null)
                return plan;
            for (int i = 0; i < scenes.Length; i++)
            {
                string guid = AssetDatabase.AssetPathToGUID(scenes[i]);
                var blockout = project.FindBlockout(guid);
                if (blockout == null)
                    continue;
                var variants = LevelComposer.FlaggedVariants(blockout);
                if (variants.Count > 0)
                    plan.Add(new PlannedLevel { SceneIndex = i, Blockout = blockout, Variants = variants });
            }
            return plan;
        }

        /// <summary>
        /// Builds with every flagged level composed (see the class summary). <paramref name="fromBuildWindow"/>: use
        /// the build window's default build method (its dialogs, Build And Run); otherwise BuildPipeline.BuildPlayer,
        /// whose report is returned. Returns null when the build was cancelled (unsaved scenes the user didn't want to
        /// save) or went through the build window.
        /// </summary>
        public static BuildReport BuildWithComposedLevels(BuildPlayerOptions options, bool fromBuildWindow = false)
        {
            var plan = Plan(options.scenes);
            if (plan.Count == 0)
                return Build(options, fromBuildWindow);

            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return null;

            var setup = LevelComposer.SaveSetup();
            bool restored = false;
            try
            {
                var scenes = (string[])options.scenes.Clone();
                foreach (var level in plan)
                {
                    string blockoutPath = scenes[level.SceneIndex];
                    string target = $"{LevelComposer.GeneratedFolder}/{LevelDesignerContext.LevelNameOf(level.Blockout)}/{Path.GetFileName(blockoutPath)}";
                    EditorUtility.DisplayProgressBar("Level Designer", "Composing " + level.Blockout.Name, 0f);
                    if (LevelComposer.Compose(level.Blockout, level.Variants, level.Blockout.ComposedBlockout, target))
                    {
                        scenes[level.SceneIndex] = target;
                        Debug.Log($"Level Designer: \"{level.Blockout.Name}\" is built as a composed scene (blockout {level.Blockout.ComposedBlockout}, + {level.Variants.Count} variant(s)).");
                    }
                    else
                        Debug.LogWarning($"Level Designer: \"{level.Blockout.Name}\" couldn't be composed; its blockout is built as it is.");
                }
                EditorUtility.ClearProgressBar();
                LevelComposer.RestoreSetup(setup);
                restored = true;

                options.scenes = scenes;
                _composing = true;
                return Build(options, fromBuildWindow);
            }
            finally
            {
                _composing = false;
                EditorUtility.ClearProgressBar();
                if (!restored)
                    LevelComposer.RestoreSetup(setup);
                LevelComposer.DeleteGenerated();
            }
        }

        private static BuildReport Build(BuildPlayerOptions options, bool fromBuildWindow)
        {
            if (fromBuildWindow)
            {
                BuildPlayerWindow.DefaultBuildMethods.BuildPlayer(options);
                return null;
            }
            return BuildPipeline.BuildPlayer(options);
        }

        /// <summary>Warns when a build includes levels that should have been composed but weren't (a scripted build).</summary>
        public void OnPreprocessBuild(BuildReport report)
        {
            if (_composing)
                return;
            var scenes = new List<string>();
            foreach (var scene in EditorBuildSettings.scenes)
                if (scene.enabled)
                    scenes.Add(scene.path);
            var plan = Plan(scenes.ToArray());
            if (plan.Count == 0)
                return;
            Debug.LogWarning("Level Designer: this build didn't go through the build window, so levels with variants flagged " +
                             "\"Compose into build\" are built as plain blockouts. Scripted builds can call " +
                             "Remedy.LevelDesigner.Editor.LevelBuildComposition.BuildWithComposedLevels(options) instead of BuildPipeline.BuildPlayer.");
        }
    }
}
