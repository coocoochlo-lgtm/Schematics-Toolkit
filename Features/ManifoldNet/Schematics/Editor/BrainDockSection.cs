using Remedy.ManifoldNetwork.Editor;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Remedy.ManifoldNetwork.Schematics.Editor
{
    /// <summary>
    /// The Brain's section in the IO Dock: its definition (created next to the prefab with one click), its input and
    /// output slots (add, rename, remove, like a Behavior's own Variables), how it's shared and how it learns, and what
    /// it has learned so far. Edits go to the definition asset, not the prefab.
    /// </summary>
    internal static class BrainDockSection
    {
        [InitializeOnLoadMethod]
        private static void Register() => BehaviorDockSections.Register(typeof(Brain), Create);

        private static VisualElement Create(BehaviorDockContext context)
        {
            var container = new IMGUIContainer(() => Draw(context));
            container.style.marginLeft = 4;
            container.style.marginRight = 4;
            container.style.marginTop = 2;
            container.style.marginBottom = 4;
            return container;
        }

        private static bool _showLearning, _showNetwork, _showLearned;

        private static void Draw(BehaviorDockContext context)
        {
            var definition = context.GetValue<BrainDefinition>("Definition");
            EditorGUIUtility.labelWidth = 110;

            if (definition == null)
            {
                EditorGUILayout.HelpBox("This Brain needs a Brain Definition: its inputs, outputs and what it learns.", MessageType.None);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Create Brain Definition"))
                        context.SetValue("Definition", CreateDefinition(context.Prefab));
                    var picked = (BrainDefinition)EditorGUILayout.ObjectField(null, typeof(BrainDefinition), false, GUILayout.Width(120));
                    if (picked != null)
                        context.SetValue("Definition", picked);
                }
                return;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                var picked = (BrainDefinition)EditorGUILayout.ObjectField(definition, typeof(BrainDefinition), false);
                if (picked != definition)
                    context.SetValue("Definition", picked);
                if (Application.isPlaying && GUILayout.Button("Debug", EditorStyles.miniButton, GUILayout.Width(48)))
                    OpenDebug(definition);
            }

            EditorGUI.BeginChangeCheck();
            Undo.RecordObject(definition, "Edit Brain");

            // Inputs.
            Header("Inputs", "What it senses. Size: how many values (a ray fan); Min-Max: their range.");
            int removeInput = -1;
            for (int i = 0; i < definition.Inputs.Count; i++)
            {
                var slot = definition.Inputs[i];
                using (new EditorGUILayout.HorizontalScope())
                {
                    slot.Name = EditorGUILayout.DelayedTextField(slot.Name, GUILayout.MinWidth(70));
                    slot.Size = Mathf.Max(1, EditorGUILayout.IntField(slot.Size, GUILayout.Width(28)));
                    slot.Min = EditorGUILayout.FloatField(slot.Min, GUILayout.Width(40));
                    slot.Max = EditorGUILayout.FloatField(slot.Max, GUILayout.Width(40));
                    if (GUILayout.Button("×", EditorStyles.miniButton, GUILayout.Width(20))) removeInput = i;
                }
            }
            if (removeInput >= 0) definition.Inputs.RemoveAt(removeInput);
            if (GUILayout.Button("+ Input", EditorStyles.miniButton))
                definition.Inputs.Add(new BrainInputSlot { Name = Unique(definition.Inputs.Select(s => s.Name), "Input") });

            // Outputs.
            Header("Outputs", "What it decides: an Axis (-1..1), a Button, or a Choice between options. The color marks it in the brain clouds.");
            int removeOutput = -1;
            for (int i = 0; i < definition.Outputs.Count; i++)
            {
                var slot = definition.Outputs[i];
                using (new EditorGUILayout.HorizontalScope())
                {
                    slot.Name = EditorGUILayout.DelayedTextField(slot.Name, GUILayout.MinWidth(70));
                    slot.Kind = (BrainOutputKind)EditorGUILayout.EnumPopup(slot.Kind, GUILayout.Width(64));
                    slot.Color = EditorGUILayout.ColorField(GUIContent.none, slot.Color, false, false, false, GUILayout.Width(28));
                    if (GUILayout.Button("×", EditorStyles.miniButton, GUILayout.Width(20))) removeOutput = i;
                }
                if (slot.Kind == BrainOutputKind.Choice)
                {
                    string options = EditorGUILayout.DelayedTextField("   Options", string.Join(", ", slot.Options));
                    slot.Options = options.Split(',').Select(o => o.Trim()).Where(o => o.Length > 0).DefaultIfEmpty("A").ToArray();
                }
            }
            if (removeOutput >= 0) definition.Outputs.RemoveAt(removeOutput);
            if (GUILayout.Button("+ Output", EditorStyles.miniButton))
                definition.Outputs.Add(new BrainOutputSlot
                {
                    Name = Unique(definition.Outputs.Select(s => s.Name), "Output"),
                    Color = Color.HSVToRGB((definition.Outputs.Count * 0.618034f) % 1f, 0.55f, 1f),
                });

            EditorGUILayout.Space(4);
            definition.Sharing = (BrainSharing)EditorGUILayout.EnumPopup(new GUIContent("Brain", "Shared: every instance of this object thinks with one brain and they learn together. Per Instance: each gets its own (they start the same)."), definition.Sharing);
            definition.TickRate = EditorGUILayout.Slider(new GUIContent("Thinks / Second"), definition.TickRate, 1f, 120f);

            _showLearning = EditorGUILayout.Foldout(_showLearning, "Learning", true);
            if (_showLearning)
            {
                EditorGUI.indentLevel++;
                definition.Learning = EditorGUILayout.Toggle("Learning", definition.Learning);
                using (new EditorGUI.DisabledScope(!definition.Learning))
                {
                    definition.TeachingWeight = EditorGUILayout.Slider(new GUIContent("Teaching", "How much Teach Brain Output answers count."), definition.TeachingWeight, 0f, 1f);
                    definition.RewardWeight = EditorGUILayout.Slider(new GUIContent("Rewards", "How much Reward Brain praise and punishment count."), definition.RewardWeight, 0f, 1f);
                    definition.HistorySeconds = EditorGUILayout.Slider(new GUIContent("History (s)", "How far back a reward can credit decisions."), definition.HistorySeconds, 0.25f, 60f);
                    definition.CreditHalfLife = EditorGUILayout.Slider(new GUIContent("Credit Half-Life (s)", "After this long, a decision gets half the credit for a reward."), definition.CreditHalfLife, 0.05f, 30f);
                    definition.Exploration = EditorGUILayout.Slider(new GUIContent("Exploration", "A little randomness while learning, so it tries new things."), definition.Exploration, 0f, 1f);
                    definition.PracticePerSecond = EditorGUILayout.Slider(new GUIContent("Practice / Second", "Lessons replayed from credited history."), definition.PracticePerSecond, 0f, 20f);
                    using (new EditorGUI.DisabledScope(definition.Sharing != BrainSharing.Shared))
                        definition.PopulationRewards = EditorGUILayout.Toggle(new GUIContent("Crowd Learning", "Shared brains: each bot also tries a slightly different version of the brain, scored by its own rewards."), definition.PopulationRewards);
                }
                EditorGUI.indentLevel--;
            }

            _showNetwork = EditorGUILayout.Foldout(_showNetwork, "Network", true);
            if (_showNetwork)
            {
                EditorGUI.indentLevel++;
                var n = definition.Network;
                n.headCount = EditorGUILayout.IntSlider(new GUIContent("Heads", "Memory heads, each moving through its own value space."), n.headCount, 1, 32);
                n.spaceDimensions = EditorGUILayout.IntSlider(new GUIContent("Space Dimensions"), n.spaceDimensions, 1, MnnSettings.MaxDimensions);
                n.pointsPerSpace = EditorGUILayout.IntSlider(new GUIContent("Points / Space"), n.pointsPerSpace, 16, 2048);
                n.hiddenWidth = EditorGUILayout.IntSlider(new GUIContent("Hidden Width"), n.hiddenWidth, 4, MnnSettings.MaxHiddenWidth);
                n.maskWidth = EditorGUILayout.IntSlider(new GUIContent("Mask Width"), n.maskWidth, 1, 32);
                n.liquidGates = EditorGUILayout.Toggle(new GUIContent("Liquid Timing", "Each head can speed up when its input says \"pay attention\". Off: fixed timescales."), n.liquidGates);
                n.wfcEnabled = EditorGUILayout.Toggle(new GUIContent("Shared Plan", "Heads commit to a coordinated multi-step plan (wave-function collapse over point types)."), n.wfcEnabled);
                n.memoryEnabled = EditorGUILayout.Toggle(new GUIContent("Episodic Memory", "Remembers teaching moments and big rewards at once, so the bot can repeat something it was shown once."), n.memoryEnabled);
                n.learningRate = EditorGUILayout.FloatField(new GUIContent("Learning Rate"), n.learningRate);
                n.seed = (uint)Mathf.Max(0, EditorGUILayout.IntField(new GUIContent("Seed", "Everything random comes from this; brains can only be mixed with the same seed."), (int)n.seed));
                definition.PreferCpu = EditorGUILayout.Toggle(new GUIContent("Prefer CPU", "Often faster for a single small brain."), definition.PreferCpu);
                if (GUILayout.Button("Advanced Settings…", EditorStyles.miniButton))
                    Selection.activeObject = definition;
                EditorGUI.indentLevel--;
            }

            _showLearned = EditorGUILayout.Foldout(_showLearned, "Learned", true) || (definition.SavedBrain != null && !definition.SavedBrainFits);
            if (_showLearned)
            {
                EditorGUI.indentLevel++;
                if (definition.SavedBrain == null || definition.SavedBrain.IsEmpty)
                    EditorGUILayout.LabelField("Nothing yet: it starts fresh from its seed.", EditorStyles.miniLabel);
                else if (!definition.SavedBrainFits)
                    EditorGUILayout.HelpBox("The slots or network settings changed, so the saved weights no longer fit (they're tied to the input and output layout). Reset it to start fresh.", MessageType.Warning);
                else
                    EditorGUILayout.LabelField($"{definition.SavedBrain.Updates:N0} training updates saved.", EditorStyles.miniLabel);
                definition.SaveOnExitPlayMode = EditorGUILayout.Toggle(new GUIContent("Save On Exit", "Keep what was learned when Play Mode ends."), definition.SaveOnExitPlayMode);
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(!Application.isPlaying))
                        if (GUILayout.Button("Save Now", EditorStyles.miniButton))
                            BrainSaving.SaveAll(definition);
                    using (new EditorGUI.DisabledScope(definition.SavedBrain == null))
                        if (GUILayout.Button("Reset", EditorStyles.miniButton) && EditorUtility.DisplayDialog("Reset Brain", $"Forget everything {definition.name} has learned?", "Reset", "Cancel"))
                            BrainSaving.Reset(definition);
                }
                EditorGUI.indentLevel--;
            }

            if (GUILayout.Button("Brain Cloud Settings…", EditorStyles.miniButton))
                SettingsService.OpenUserPreferences("Preferences/Manifold Network Gizmos");

            if (EditorGUI.EndChangeCheck())
            {
                EditorUtility.SetDirty(definition);
                AssetDatabase.SaveAssetIfDirty(definition);
            }
        }

        private static void Header(string title, string tooltip)
        {
            EditorGUILayout.Space(3);
            EditorGUILayout.LabelField(new GUIContent(title, tooltip), EditorStyles.miniBoldLabel);
        }

        private static string Unique(System.Collections.Generic.IEnumerable<string> taken, string name)
        {
            var set = taken.ToHashSet();
            if (!set.Contains(name)) return name;
            for (int i = 2; ; i++)
                if (!set.Contains($"{name} {i}")) return $"{name} {i}";
        }

        private static BrainDefinition CreateDefinition(GameObject prefab)
        {
            string prefabPath = prefab != null ? AssetDatabase.GetAssetPath(prefab) : "";
            string folder = string.IsNullOrEmpty(prefabPath) ? "Assets" : Path.GetDirectoryName(prefabPath).Replace('\\', '/');
            string name = prefab != null ? prefab.name + " Brain" : "Brain";
            string path = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{name}.asset");
            var definition = ScriptableObject.CreateInstance<BrainDefinition>();
            AssetDatabase.CreateAsset(definition, path);
            AssetDatabase.SaveAssets();
            return definition;
        }

        private static void OpenDebug(BrainDefinition definition)
        {
            foreach (var network in BrainHub.All)
                if (network.Definition == definition)
                {
                    MnnDebugWindow.Open(network.Net, 0);
                    return;
                }
            MnnDebugWindow.Open();
        }
    }

    /// <summary>Keeps what brains learned: into their definition's Saved Brain (a sub-asset).</summary>
    [InitializeOnLoad]
    internal static class BrainSaving
    {
        static BrainSaving()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state != PlayModeStateChange.ExitingPlayMode) return;
                foreach (var network in BrainHub.All.ToList())
                    if (network.Definition != null && network.Definition.SaveOnExitPlayMode)
                        Save(network);
            };
            BrainHub.BeforeDispose += network =>
            {
                if (Application.isPlaying && network.Definition != null && network.Definition.SaveOnExitPlayMode)
                    Save(network);
            };
        }

        public static void SaveAll(BrainDefinition definition)
        {
            foreach (var network in BrainHub.All.ToList())
                if (network.Definition == definition)
                {
                    Save(network);
                    return; // per-instance brains: the first one's
                }
        }

        private static void Save(BrainNetwork network)
        {
            var definition = network.Definition;
            if (network.Net == null || network.Net.Disposed || !EditorUtility.IsPersistent(definition))
                return;
            if (definition.SavedBrain == null)
            {
                var brain = network.Net.SaveBrain();
                brain.name = "Learned";
                AssetDatabase.AddObjectToAsset(brain, definition);
                definition.SavedBrain = brain;
            }
            else
                network.Net.SaveBrain(definition.SavedBrain);
            EditorUtility.SetDirty(definition.SavedBrain);
            EditorUtility.SetDirty(definition);
            AssetDatabase.SaveAssetIfDirty(definition);
        }

        public static void Reset(BrainDefinition definition)
        {
            if (definition.SavedBrain != null)
            {
                Undo.RecordObject(definition, "Reset Brain");
                var brain = definition.SavedBrain;
                definition.SavedBrain = null;
                if (AssetDatabase.IsSubAsset(brain))
                    Object.DestroyImmediate(brain, true);
            }
            EditorUtility.SetDirty(definition);
            AssetDatabase.SaveAssetIfDirty(definition);
        }
    }

    /// <summary>The slot name on brain nodes, as a dropdown of the slots of the Brains on the graph's prefab.</summary>
    [CustomPropertyDrawer(typeof(BrainSlotAttribute))]
    internal class BrainSlotDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var output = ((BrainSlotAttribute)attribute).Output;
            var names = SlotNames(property.serializedObject.targetObject, output);
            if (names.Length == 0)
            {
                EditorGUI.PropertyField(position, property, label);
                return;
            }
            int index = System.Array.IndexOf(names, property.stringValue);
            var options = index < 0 ? names.Prepend(property.stringValue + " (missing)").ToArray() : names;
            int shown = index < 0 ? 0 : index;
            int picked = EditorGUI.Popup(position, label.text, shown, options);
            if (picked != shown)
                property.stringValue = index < 0 ? names[picked - 1] : names[picked];
        }

        private static string[] SlotNames(Object target, bool output)
        {
            var graph = target as Remedy.Schematics.SchematicGraph;
            var prefab = graph != null ? graph.Prefab : null;
            var root = prefab != null ? prefab.GetComponent<Remedy.Schematics.SchematicObject>() : null;
            if (root == null)
                return System.Array.Empty<string>();
            var names = new System.Collections.Generic.List<string>();
            foreach (var pair in root.Behaviors)
            {
                if (pair.Value == null) continue;
                foreach (var attachment in pair.Value.Items)
                {
                    if (attachment?.Metadata == null || attachment.Metadata.BehaviorType.Type != typeof(Brain)) continue;
                    var entry = attachment.FindOverride("Definition");
                    var definition = entry?.Value.Get(typeof(BrainDefinition)) as BrainDefinition;
                    if (definition == null) continue;
                    names.AddRange(output ? definition.Outputs.Select(s => s.Name) : definition.Inputs.Select(s => s.Name));
                }
            }
            return names.Distinct().ToArray();
        }
    }

    /// <summary>Preferences > Manifold Network Gizmos: how the clouds over agents' heads look.</summary>
    [InitializeOnLoad]
    internal static class BrainCloudPreferences
    {
        private const string Prefix = "Remedy.ManifoldNet.Clouds.";

        static BrainCloudPreferences() => Load();

        private static void Load()
        {
            BrainCloudSettings.Show = EditorPrefs.GetBool(Prefix + "Show", true);
            BrainCloudSettings.Scale = EditorPrefs.GetFloat(Prefix + "Scale", 0.35f);
            BrainCloudSettings.PointSize = EditorPrefs.GetFloat(Prefix + "PointSize", 0.025f);
            BrainCloudSettings.HeadSize = EditorPrefs.GetFloat(Prefix + "HeadSize", 0.08f);
            BrainCloudSettings.PointOpacity = EditorPrefs.GetFloat(Prefix + "PointOpacity", 0.35f);
            BrainCloudSettings.TailLength = EditorPrefs.GetInt(Prefix + "TailLength", 24);
            BrainCloudSettings.SelectedOnly = EditorPrefs.GetBool(Prefix + "SelectedOnly", false);
            BrainCloudSettings.MaxDistance = EditorPrefs.GetFloat(Prefix + "MaxDistance", 40f);
            BrainCloudSettings.ShowInGameView = EditorPrefs.GetBool(Prefix + "ShowInGameView", false);
            BrainCloudSettings.Gap = EditorPrefs.GetFloat(Prefix + "Gap", 0.3f);
        }

        private static void Save()
        {
            EditorPrefs.SetBool(Prefix + "Show", BrainCloudSettings.Show);
            EditorPrefs.SetFloat(Prefix + "Scale", BrainCloudSettings.Scale);
            EditorPrefs.SetFloat(Prefix + "PointSize", BrainCloudSettings.PointSize);
            EditorPrefs.SetFloat(Prefix + "HeadSize", BrainCloudSettings.HeadSize);
            EditorPrefs.SetFloat(Prefix + "PointOpacity", BrainCloudSettings.PointOpacity);
            EditorPrefs.SetInt(Prefix + "TailLength", BrainCloudSettings.TailLength);
            EditorPrefs.SetBool(Prefix + "SelectedOnly", BrainCloudSettings.SelectedOnly);
            EditorPrefs.SetFloat(Prefix + "MaxDistance", BrainCloudSettings.MaxDistance);
            EditorPrefs.SetBool(Prefix + "ShowInGameView", BrainCloudSettings.ShowInGameView);
            EditorPrefs.SetFloat(Prefix + "Gap", BrainCloudSettings.Gap);
        }

        [SettingsProvider]
        private static SettingsProvider Provider() => new("Preferences/Manifold Network Gizmos", SettingsScope.User)
        {
            keywords = new System.Collections.Generic.HashSet<string> { "brain", "cloud", "orb", "manifold", "gizmo" },
            guiHandler = search =>
            {
                EditorGUIUtility.labelWidth = 170;
                EditorGUI.BeginChangeCheck();
                BrainCloudSettings.Show = EditorGUILayout.Toggle("Show Brain Clouds", BrainCloudSettings.Show);
                using (new EditorGUI.DisabledScope(!BrainCloudSettings.Show))
                {
                    BrainCloudSettings.Scale = EditorGUILayout.Slider("Scale", BrainCloudSettings.Scale, 0.05f, 3f);
                    BrainCloudSettings.Gap = EditorGUILayout.Slider(new GUIContent("Height Above Bounds"), BrainCloudSettings.Gap, 0f, 3f);
                    BrainCloudSettings.HeadSize = EditorGUILayout.Slider("Head Orb Size", BrainCloudSettings.HeadSize, 0.01f, 0.5f);
                    BrainCloudSettings.PointSize = EditorGUILayout.Slider("Point Orb Size", BrainCloudSettings.PointSize, 0.005f, 0.2f);
                    BrainCloudSettings.PointOpacity = EditorGUILayout.Slider("Point Opacity", BrainCloudSettings.PointOpacity, 0f, 1f);
                    BrainCloudSettings.TailLength = EditorGUILayout.IntSlider("Tail Length (ticks)", BrainCloudSettings.TailLength, 1, 128);
                    BrainCloudSettings.SelectedOnly = EditorGUILayout.Toggle("Selected Objects Only", BrainCloudSettings.SelectedOnly);
                    BrainCloudSettings.MaxDistance = EditorGUILayout.FloatField("Max Distance", BrainCloudSettings.MaxDistance);
                    BrainCloudSettings.ShowInGameView = EditorGUILayout.Toggle(new GUIContent("Show In Game View", "Scene views always show them."), BrainCloudSettings.ShowInGameView);
                }
                if (EditorGUI.EndChangeCheck())
                    Save();
            },
        };
    }
}
