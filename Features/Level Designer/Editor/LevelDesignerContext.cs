using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Remedy.LevelDesigner.Editor
{
    /// <summary>What the Scene view is editing right now, as far as the Level Designer is concerned.</summary>
    public enum LevelDesignerMode
    {
        /// <summary>Neither a blockout nor an art design is the active scene (or Play Mode is on).</summary>
        None,
        /// <summary>The active scene is a blockout: the blockout tools are shown.</summary>
        Blockout,
        /// <summary>The active scene is an art design: its blockout is shown as an X-ray.</summary>
        ArtDesign,
    }

    /// <summary>
    /// The Level Designer's bookkeeping in the editor: which mode the active scene puts it in, the project's
    /// <see cref="LevelDesignProject"/>, and creating and opening blockout and art design scenes.
    /// </summary>
    [InitializeOnLoad]
    public static class LevelDesignerContext
    {
        /// <summary>Where the Level Designer's own data goes (the project asset, the blockout material). Scenes go in
        /// <see cref="LevelsRoot"/> instead.</summary>
        public const string DefaultFolder = "Assets/Level Design";

        /// <summary>The current mode.</summary>
        public static LevelDesignerMode Mode { get; private set; }

        /// <summary>The blockout marker of the active scene (Blockout mode), or of the blockout under the art (Art mode, if loaded).</summary>
        public static BlockoutScene Blockout { get; private set; }

        /// <summary>The art design marker of the active scene (Art mode).</summary>
        public static ArtDesignScene ArtDesign { get; private set; }

        /// <summary>The blockout scene loaded under the active art design (invalid when it isn't loaded).</summary>
        public static Scene BlockoutSceneUnderArt { get; private set; }

        private static Scene _lastActive;

        /// <summary>Raised when the mode or the scenes involved change.</summary>
        public static event Action ModeChanged;

        static LevelDesignerContext()
        {
            EditorSceneManager.activeSceneChangedInEditMode += (_, _) => Refresh();
            EditorSceneManager.sceneOpened += (_, _) => Refresh();
            EditorSceneManager.sceneClosed += _ => Refresh();
            EditorSceneManager.newSceneCreated += (_, _, _) => Refresh();
            EditorApplication.playModeStateChanged += _ => Refresh();
            EditorApplication.hierarchyChanged += RefreshIfMarkersChanged;
            EditorApplication.delayCall += Refresh;
        }

        private static void RefreshIfMarkersChanged()
        {
            // Cheap check: only re-evaluate when a marker was added or removed in the active scene.
            var active = SceneManager.GetActiveScene();
            bool hasBlockout = Find<BlockoutScene>(active) != null;
            bool hasArt = Find<ArtDesignScene>(active) != null;
            var expected = hasArt ? LevelDesignerMode.ArtDesign : hasBlockout ? LevelDesignerMode.Blockout : LevelDesignerMode.None;
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                expected = LevelDesignerMode.None;
            if (expected != Mode || (Mode == LevelDesignerMode.ArtDesign && !BlockoutSceneUnderArt.IsValid() && FindLoadedBlockoutFor(ArtDesign).IsValid()))
                Refresh();
        }

        /// <summary>Works the mode out again from the loaded scenes.</summary>
        public static void Refresh()
        {
            var previousMode = Mode;
            var previousBlockout = BlockoutSceneUnderArt;
            var previousActive = _lastActive;
            _lastActive = SceneManager.GetActiveScene();

            Mode = LevelDesignerMode.None;
            Blockout = null;
            ArtDesign = null;
            BlockoutSceneUnderArt = default;

            if (!EditorApplication.isPlayingOrWillChangePlaymode)
            {
                var active = SceneManager.GetActiveScene();
                ArtDesign = Find<ArtDesignScene>(active);
                if (ArtDesign != null)
                {
                    Mode = LevelDesignerMode.ArtDesign;
                    BlockoutSceneUnderArt = FindLoadedBlockoutFor(ArtDesign);
                    if (BlockoutSceneUnderArt.IsValid())
                        Blockout = Find<BlockoutScene>(BlockoutSceneUnderArt);
                    FixBlockoutPath(ArtDesign);
                }
                else
                {
                    Blockout = Find<BlockoutScene>(active);
                    if (Blockout != null)
                        Mode = LevelDesignerMode.Blockout;
                }
            }

            if (previousMode != Mode || previousBlockout != BlockoutSceneUnderArt || previousActive != SceneManager.GetActiveScene())
                ModeChanged?.Invoke();
            SceneView.RepaintAll();
        }

        // The art marker keeps the blockout's path for Play Mode loading; when the blockout was moved or renamed, the
        // GUID still finds it, so the path is brought up to date (builds and Play Mode also fix it on the fly, see
        // ArtDesignScenePathFixer).
        private static void FixBlockoutPath(ArtDesignScene art)
        {
            if (art == null || string.IsNullOrEmpty(art.BlockoutSceneGuid))
                return;
            string path = AssetDatabase.GUIDToAssetPath(art.BlockoutSceneGuid);
            if (string.IsNullOrEmpty(path) || path == art.BlockoutScenePath)
                return;
            art.BlockoutScenePath = path;
            EditorUtility.SetDirty(art);
            EditorSceneManager.MarkSceneDirty(art.gameObject.scene);
        }

        /// <summary>The first component of a type in a scene's objects (inactive ones too), or null.</summary>
        public static T Find<T>(Scene scene) where T : Component
        {
            if (!scene.IsValid() || !scene.isLoaded)
                return null;
            foreach (var root in scene.GetRootGameObjects())
            {
                var found = root.GetComponentInChildren<T>(true);
                if (found != null)
                    return found;
            }
            return null;
        }

        /// <summary>The loaded scene holding the blockout an art design was built on, or an invalid scene.</summary>
        public static Scene FindLoadedBlockoutFor(ArtDesignScene art)
        {
            if (art == null)
                return default;
            string path = ResolvePath(art.BlockoutSceneGuid, art.BlockoutScenePath);
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (scene.isLoaded && scene != art.gameObject.scene && !string.IsNullOrEmpty(path) && scene.path == path)
                    return scene;
            }
            return default;
        }

        // ---------------------------------------------------------------- project asset

        private static LevelDesignProject _project;

        /// <summary>The project's level list: the first LevelDesignProject asset found, made if there's none.</summary>
        public static LevelDesignProject Project
        {
            get
            {
                if (_project != null)
                    return _project;
                foreach (var guid in AssetDatabase.FindAssets("t:" + nameof(LevelDesignProject)))
                {
                    _project = AssetDatabase.LoadAssetAtPath<LevelDesignProject>(AssetDatabase.GUIDToAssetPath(guid));
                    if (_project != null)
                        return _project;
                }

                EnsureFolder(DefaultFolder);
                _project = ScriptableObject.CreateInstance<LevelDesignProject>();
                AssetDatabase.CreateAsset(_project, DefaultFolder + "/LevelDesignProject.asset");
                AssetDatabase.SaveAssets();
                return _project;
            }
        }

        /// <summary>Records a change to the project asset (undoable) before making it.</summary>
        public static void RecordProject(string what)
        {
            Undo.RecordObject(Project, what);
            EditorUtility.SetDirty(Project);
        }

        /// <summary>A scene's current path from its GUID, falling back to the remembered path.</summary>
        public static string ResolvePath(string guid, string fallbackPath)
        {
            if (!string.IsNullOrEmpty(guid))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    return path;
            }
            return fallbackPath;
        }

        public static string PathOf(BlockoutLevel blockout) => blockout == null ? null : ResolvePath(blockout.SceneGuid, blockout.ScenePath);
        public static string PathOf(ArtDesign art) => art == null ? null : ResolvePath(art.SceneGuid, art.ScenePath);

        /// <summary>The blockout entry for the active blockout scene or the blockout under the active art design.</summary>
        public static BlockoutLevel CurrentBlockoutEntry
        {
            get
            {
                if (Mode == LevelDesignerMode.Blockout)
                    return Project.FindBlockout(AssetDatabase.AssetPathToGUID(SceneManager.GetActiveScene().path));
                if (Mode == LevelDesignerMode.ArtDesign && ArtDesign != null)
                    return Project.FindBlockout(ArtDesign.BlockoutSceneGuid);
                return null;
            }
        }

        // ---------------------------------------------------------------- creating and opening scenes

        // ---------------------------------------------------------------- folder layout

        /// <summary>
        /// The folder every Level Designer scene goes in (the project asset's <see cref="LevelDesignProject.LevelsRootFolder"/>,
        /// Assets/Levels by default). Layout:
        /// <code>
        /// &lt;Root&gt;/&lt;Level&gt;/&lt;Level&gt;_Blockout.unity
        /// &lt;Root&gt;/&lt;Level&gt;/Art/&lt;Level&gt;_&lt;Art Design&gt;.unity
        /// &lt;Root&gt;/Thumbnails~/&lt;scene GUID&gt;.png
        /// </code>
        /// One folder per level keeps a level's blockout and all its art together; the art designs get their own
        /// subfolder so the blockout is easy to spot, and the level name prefix keeps scene names unique in
        /// searches and the Build Profile.
        /// </summary>
        public static string LevelsRoot
        {
            get
            {
                string root = Project.LevelsRootFolder;
                if (string.IsNullOrWhiteSpace(root) || !(root == "Assets" || root.StartsWith("Assets/")))
                    root = LevelDesignProject.DefaultLevelsRootFolder;
                return root.TrimEnd('/');
            }
        }

        /// <summary>Changes the levels root (undoable). Thumbnails move along; scenes stay until moved explicitly.</summary>
        public static void SetLevelsRoot(string folder)
        {
            folder = folder?.Replace('\\', '/').TrimEnd('/');
            if (string.IsNullOrEmpty(folder) || !(folder == "Assets" || folder.StartsWith("Assets/")))
                return;
            string old = LevelsRoot;
            if (old == folder)
                return;
            RecordProject("Change Levels Folder");
            Project.LevelsRootFolder = folder;
            AssetDatabase.SaveAssets();
            LevelThumbnails.MoveFolder(old, folder);
        }

        /// <summary>
        /// The level's name for folder and file names: the blockout scene's name without "_Blockout" when it follows
        /// the layout, otherwise the blockout's display name (or its scene name).
        /// </summary>
        public static string LevelNameOf(BlockoutLevel blockout)
        {
            string path = PathOf(blockout);
            string fileName = !string.IsNullOrEmpty(path) ? Path.GetFileNameWithoutExtension(path) : null;
            string name;
            if (!string.IsNullOrEmpty(fileName) && fileName.EndsWith(BlockoutSuffix, StringComparison.OrdinalIgnoreCase) && fileName.Length > BlockoutSuffix.Length)
                name = fileName.Substring(0, fileName.Length - BlockoutSuffix.Length);
            else
                name = !string.IsNullOrWhiteSpace(blockout?.Name) ? blockout.Name : fileName;
            return SanitizeFileName(string.IsNullOrWhiteSpace(name) ? "Level" : name.Trim());
        }

        private const string BlockoutSuffix = "_Blockout";

        /// <summary>Where a blockout's scene belongs in the layout.</summary>
        public static string LayoutPathOf(BlockoutLevel blockout)
        {
            string level = LevelNameOf(blockout);
            return $"{LevelsRoot}/{level}/{level}{BlockoutSuffix}.unity";
        }

        /// <summary>Where an art design's scene belongs in the layout.</summary>
        public static string LayoutPathOf(BlockoutLevel blockout, ArtDesign art)
        {
            string level = LevelNameOf(blockout);
            return $"{LevelsRoot}/{level}/Art/{level}_{SanitizeFileName(string.IsNullOrWhiteSpace(art?.Name) ? "Art" : art.Name.Trim())}.unity";
        }

        /// <summary>The folder a blockout's art designs go in: "Art" next to the blockout scene.</summary>
        public static string ArtFolderOf(BlockoutLevel blockout)
        {
            string blockoutPath = PathOf(blockout);
            string folder = !string.IsNullOrEmpty(blockoutPath)
                ? Path.GetDirectoryName(blockoutPath)?.Replace('\\', '/')
                : LevelsRoot + "/" + LevelNameOf(blockout);
            return (string.IsNullOrEmpty(folder) ? LevelsRoot : folder) + "/Art";
        }

        // ---------------------------------------------------------------- creating and opening scenes

        /// <summary>
        /// Makes a new blockout scene (camera, light, a 20 x 20 m floor and the blockout marker) at
        /// &lt;Levels Root&gt;/&lt;Level&gt;/&lt;Level&gt;_Blockout.unity (no save dialog), adds it to the project and opens it.
        /// An empty name picks "Level 1", "Level 2"… whichever is free.
        /// </summary>
        public static BlockoutLevel CreateBlockout(string levelName = null)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return null;

            string root = LevelsRoot;
            string level = string.IsNullOrWhiteSpace(levelName) ? null : SanitizeFileName(levelName.Trim());
            if (level == null)
            {
                int number = Project.Blockouts.Count + 1;
                while (AssetDatabase.IsValidFolder($"{root}/Level {number}"))
                    number++;
                level = "Level " + number;
            }
            else if (AssetDatabase.IsValidFolder($"{root}/{level}") && File.Exists($"{root}/{level}/{level}{BlockoutSuffix}.unity"))
            {
                // That level exists: number the new one rather than overwrite it.
                int number = 2;
                while (AssetDatabase.IsValidFolder($"{root}/{level} {number}"))
                    number++;
                level = $"{level} {number}";
            }

            string folder = $"{root}/{level}";
            EnsureFolder(folder);
            string path = $"{folder}/{level}{BlockoutSuffix}.unity";

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            var marker = new GameObject("[Blockout]");
            marker.AddComponent<BlockoutScene>();

            var floor = new GameObject("Floor");
            floor.transform.position = new Vector3(0f, -0.5f, 0f);
            var shape = floor.AddComponent<BlockoutShape>();
            shape.Size = new Vector3(20f, 0.5f, 20f);
            shape.Category = BlockoutCategory.Floor;
            shape.Material = BlockoutMaterial;
            shape.Rebuild();

            EditorSceneManager.SaveScene(scene, path);

            var entry = new BlockoutLevel
            {
                Name = level,
                SceneGuid = AssetDatabase.AssetPathToGUID(path),
                ScenePath = path,
            };
            RecordProject("New Blockout");
            Project.Blockouts.Add(entry);
            AssetDatabase.SaveAssets();
            Refresh();
            return entry;
        }

        /// <summary>Adds an existing scene to the project as a blockout (adding the blockout marker to it if it's open).</summary>
        public static BlockoutLevel AddExistingBlockout(string path)
        {
            if (string.IsNullOrEmpty(path))
                return null;
            string guid = AssetDatabase.AssetPathToGUID(path);
            var existing = Project.FindBlockout(guid);
            if (existing != null)
                return existing;

            var entry = new BlockoutLevel { Name = Path.GetFileNameWithoutExtension(path), SceneGuid = guid, ScenePath = path };
            RecordProject("Add Blockout");
            Project.Blockouts.Add(entry);
            AssetDatabase.SaveAssets();
            return entry;
        }

        /// <summary>Opens a blockout scene on its own.</summary>
        public static bool OpenBlockout(BlockoutLevel blockout)
        {
            string path = PathOf(blockout);
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                EditorUtility.DisplayDialog("Level Designer", $"The blockout scene for \"{blockout?.Name}\" can't be found.", "OK");
                return false;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return false;
            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            EnsureBlockoutMarker(scene);
            Refresh();
            return true;
        }

        /// <summary>Opens an art design with its blockout loaded underneath, the art design being the active scene.</summary>
        public static bool OpenArtDesign(BlockoutLevel blockout, ArtDesign art)
        {
            string blockoutPath = PathOf(blockout);
            string artPath = PathOf(art);
            if (string.IsNullOrEmpty(artPath) || !File.Exists(artPath))
            {
                EditorUtility.DisplayDialog("Level Designer", $"The art design scene for \"{art?.Name}\" can't be found.", "OK");
                return false;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return false;

            Scene artScene;
            if (!string.IsNullOrEmpty(blockoutPath) && File.Exists(blockoutPath))
            {
                EditorSceneManager.OpenScene(blockoutPath, OpenSceneMode.Single);
                artScene = EditorSceneManager.OpenScene(artPath, OpenSceneMode.Additive);
            }
            else
            {
                artScene = EditorSceneManager.OpenScene(artPath, OpenSceneMode.Single);
            }
            SceneManager.SetActiveScene(artScene);
            Refresh();
            return true;
        }

        /// <summary>Loads the active art design's blockout underneath it (when the art scene was opened on its own).</summary>
        public static void LoadBlockoutUnderActiveArt()
        {
            if (ArtDesign == null)
                return;
            string path = ResolvePath(ArtDesign.BlockoutSceneGuid, ArtDesign.BlockoutScenePath);
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                EditorUtility.DisplayDialog("Level Designer", "This art design's blockout scene can't be found.", "OK");
                return;
            }
            var active = SceneManager.GetActiveScene();
            EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            SceneManager.SetActiveScene(active);
            Refresh();
        }

        /// <summary>
        /// Makes a new, empty art design scene next to the blockout (with a marker remembering the blockout), adds it
        /// to the project and opens it over the blockout.
        /// </summary>
        public static ArtDesign CreateArtDesign(BlockoutLevel blockout, string name)
        {
            string blockoutPath = PathOf(blockout);
            if (string.IsNullOrEmpty(blockoutPath) || !File.Exists(blockoutPath))
            {
                EditorUtility.DisplayDialog("Level Designer", "The blockout scene can't be found, so an art design can't be made for it.", "OK");
                return null;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return null;

            name = string.IsNullOrWhiteSpace(name) ? "Art Design " + (blockout.ArtDesigns.Count + 1) : name.Trim();
            // <blockout folder>/Art/<Level>_<Art Design>.unity
            string folder = ArtFolderOf(blockout);
            EnsureFolder(folder);
            string fileName = LevelNameOf(blockout) + "_" + SanitizeFileName(name) + ".unity";
            string artPath = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + fileName);

            // Open the blockout first: a new scene can only be added next to saved scenes.
            EditorSceneManager.OpenScene(blockoutPath, OpenSceneMode.Single);
            var artScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(artScene);
            var marker = new GameObject("[Art Design]");
            var component = marker.AddComponent<ArtDesignScene>();
            component.BlockoutSceneGuid = blockout.SceneGuid;
            component.BlockoutScenePath = blockoutPath;
            EditorSceneManager.SaveScene(artScene, artPath);

            var art = new ArtDesign { Name = name, SceneGuid = AssetDatabase.AssetPathToGUID(artPath), ScenePath = artPath };
            RecordProject("New Art Design");
            blockout.ArtDesigns.Add(art);
            AssetDatabase.SaveAssets();
            Refresh();
            return art;
        }

        /// <summary>Copies an art design's scene into a new art design of the same blockout.</summary>
        public static ArtDesign DuplicateArtDesign(BlockoutLevel blockout, ArtDesign source)
        {
            string sourcePath = PathOf(source);
            if (string.IsNullOrEmpty(sourcePath) || !File.Exists(sourcePath))
                return null;
            string copyName = (string.IsNullOrWhiteSpace(source.Name) ? "Art" : source.Name.Trim()) + " Copy";
            string folder = ArtFolderOf(blockout);
            EnsureFolder(folder);
            string copyPath = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{LevelNameOf(blockout)}_{SanitizeFileName(copyName)}.unity");
            if (!AssetDatabase.CopyAsset(sourcePath, copyPath))
                return null;

            var art = new ArtDesign
            {
                Name = copyName,
                SceneGuid = AssetDatabase.AssetPathToGUID(copyPath),
                ScenePath = copyPath,
                Notes = source.Notes,
            };
            RecordProject("Duplicate Art Design");
            blockout.ArtDesigns.Insert(blockout.ArtDesigns.IndexOf(source) + 1, art);
            AssetDatabase.SaveAssets();
            return art;
        }

        /// <summary>Removes an art design from the project, optionally moving its scene to the trash.</summary>
        public static void RemoveArtDesign(BlockoutLevel blockout, ArtDesign art, bool deleteScene)
        {
            RecordProject("Remove Art Design");
            blockout.ArtDesigns.Remove(art);
            string path = PathOf(art);
            if (deleteScene && !string.IsNullOrEmpty(path) && File.Exists(path) && !IsLoaded(path))
                AssetDatabase.MoveAssetToTrash(path);
            AssetDatabase.SaveAssets();
        }

        /// <summary>Removes a blockout (and its art design list) from the project, optionally trashing the scenes.</summary>
        public static void RemoveBlockout(BlockoutLevel blockout, bool deleteScenes)
        {
            RecordProject("Remove Blockout");
            Project.Blockouts.Remove(blockout);
            if (deleteScenes)
            {
                foreach (var art in blockout.ArtDesigns)
                {
                    string artPath = PathOf(art);
                    if (!string.IsNullOrEmpty(artPath) && File.Exists(artPath) && !IsLoaded(artPath))
                        AssetDatabase.MoveAssetToTrash(artPath);
                }
                string path = PathOf(blockout);
                if (!string.IsNullOrEmpty(path) && File.Exists(path) && !IsLoaded(path))
                    AssetDatabase.MoveAssetToTrash(path);
            }
            AssetDatabase.SaveAssets();
        }

        /// <summary>A planned move of one scene into the folder layout.</summary>
        public struct LayoutMove
        {
            public string From;
            public string To;
            public BlockoutLevel Blockout;
            /// <summary>Null when the scene is the blockout itself.</summary>
            public ArtDesign Art;
        }

        /// <summary>The scenes that aren't where the folder layout would put them (missing scenes are skipped).</summary>
        public static List<LayoutMove> PlanLayoutMoves()
        {
            var moves = new List<LayoutMove>();
            foreach (var blockout in Project.Blockouts)
            {
                if (blockout == null)
                    continue;
                string path = PathOf(blockout);
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    string target = LayoutPathOf(blockout);
                    if (target != path)
                        moves.Add(new LayoutMove { From = path, To = target, Blockout = blockout });
                }
                foreach (var art in blockout.ArtDesigns)
                {
                    string artPath = PathOf(art);
                    if (art == null || string.IsNullOrEmpty(artPath) || !File.Exists(artPath))
                        continue;
                    string target = LayoutPathOf(blockout, art);
                    if (target != artPath)
                        moves.Add(new LayoutMove { From = artPath, To = target, Blockout = blockout, Art = art });
                }
            }
            return moves;
        }

        /// <summary>
        /// Moves every listed blockout and art design scene into the folder layout under <see cref="LevelsRoot"/>,
        /// with AssetDatabase.MoveAsset, so GUIDs (and every reference to the scenes) are kept. Asks first, saves
        /// open scenes first, never overwrites: a scene whose target is taken is left where it is and reported.
        /// Old folders are left in place (they may hold other assets). Returns how many scenes were moved.
        /// </summary>
        public static int MoveScenesIntoLayout()
        {
            var moves = PlanLayoutMoves();
            if (moves.Count == 0)
            {
                EditorUtility.DisplayDialog("Level Designer", $"Every scene is already in the layout under {LevelsRoot}.", "OK");
                return 0;
            }

            var preview = new StringBuilder();
            for (int i = 0; i < moves.Count && i < 12; i++)
                preview.AppendLine($"{moves[i].From}\n    → {moves[i].To}");
            if (moves.Count > 12)
                preview.AppendLine($"…and {moves.Count - 12} more.");
            if (!EditorUtility.DisplayDialog("Move Scenes into the Levels Folder",
                    $"Move {moves.Count} scene(s) into {LevelsRoot}?\n\n{preview}\nScenes keep their GUIDs, so references and the " +
                    "Level Designer's list stay intact. Open scenes are saved first. Undo isn't available for moves, but " +
                    "version control has the old paths.", "Move", "Cancel"))
                return 0;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return 0;

            int moved = 0;
            var problems = new StringBuilder();
            RecordProject("Move Scenes into Levels Folder");
            try
            {
                for (int i = 0; i < moves.Count; i++)
                {
                    var move = moves[i];
                    EditorUtility.DisplayProgressBar("Level Designer", move.From, (float)i / moves.Count);
                    if (File.Exists(move.To))
                    {
                        problems.AppendLine($"{move.From}: {move.To} already exists");
                        continue;
                    }
                    EnsureFolder(Path.GetDirectoryName(move.To)?.Replace('\\', '/'));
                    string error = AssetDatabase.ValidateMoveAsset(move.From, move.To);
                    if (string.IsNullOrEmpty(error))
                        error = AssetDatabase.MoveAsset(move.From, move.To);
                    if (!string.IsNullOrEmpty(error))
                    {
                        problems.AppendLine($"{move.From}: {error}");
                        continue;
                    }
                    if (move.Art != null)
                        move.Art.ScenePath = move.To;
                    else
                        move.Blockout.ScenePath = move.To;
                    moved++;
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
            EditorUtility.SetDirty(Project);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Refresh();

            if (problems.Length > 0)
                EditorUtility.DisplayDialog("Level Designer", $"Moved {moved} of {moves.Count} scene(s). Not moved:\n\n{problems}", "OK");
            return moved;
        }

        private static bool IsLoaded(string path)
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).path == path)
                    return true;
            return false;
        }

        private static void EnsureBlockoutMarker(Scene scene)
        {
            if (Find<BlockoutScene>(scene) != null)
                return;
            var marker = new GameObject("[Blockout]");
            SceneManager.MoveGameObjectToScene(marker, scene);
            marker.AddComponent<BlockoutScene>();
            EditorSceneManager.MarkSceneDirty(scene);
        }

        private static string SanitizeFileName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name;
        }

        /// <summary>Makes a folder (and its parents) in the project if it doesn't exist.</summary>
        public static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder))
                return;
            string parent = Path.GetDirectoryName(folder)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
                EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }

        // ---------------------------------------------------------------- materials

        private static Material _blockoutMaterial;
        private static Material _xrayMaterial;

        /// <summary>
        /// The shared blockout material, saved as an asset (so builds include the shader). Made on first use in
        /// <see cref="DefaultFolder"/>/Materials.
        /// </summary>
        public static Material BlockoutMaterial
        {
            get
            {
                if (_blockoutMaterial != null)
                    return _blockoutMaterial;
                string path = DefaultFolder + "/Materials/Blockout.mat";
                _blockoutMaterial = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (_blockoutMaterial == null)
                {
                    var shader = Shader.Find(BlockoutShape.ShaderName);
                    if (shader == null)
                    {
                        Debug.LogWarning("Level Designer: the blockout shader wasn't found; shapes use the default material.");
                        return null;
                    }
                    EnsureFolder(DefaultFolder + "/Materials");
                    _blockoutMaterial = new Material(shader) { name = "Blockout" };
                    _blockoutMaterial.enableInstancing = true;
                    AssetDatabase.CreateAsset(_blockoutMaterial, path);
                }
                return _blockoutMaterial;
            }
        }

        /// <summary>The X-ray overlay's material (editor-only, not saved).</summary>
        public static Material XRayMaterial
        {
            get
            {
                if (_xrayMaterial == null)
                {
                    var shader = Shader.Find("Remedy/Level Designer/Blockout X-Ray");
                    if (shader != null)
                        _xrayMaterial = new Material(shader) { name = "Blockout X-Ray", hideFlags = HideFlags.HideAndDontSave };
                }
                return _xrayMaterial;
            }
        }
    }
}
