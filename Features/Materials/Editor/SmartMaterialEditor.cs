using BlueGraph;
using BlueGraph.Editor;
using Remedy.Meshes;
using Remedy.Schematics;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

namespace Remedy.Materials
{
    /// <summary>
    /// Smart Materials in the editor: making them (with a starter layer stack), a live preview in the corner of the
    /// graph window, and baking to textures (PNG files next to the graph) and a material for this project's render
    /// pipeline.
    /// </summary>
    [InitializeOnLoad]
    internal static class SmartMaterialEditor
    {
        static SmartMaterialEditor()
        {
            // Smart Material nodes only show in Smart Materials.
            DefaultSearchProvider.ExclusiveTags.Add(MaterialNode.Tag);
            DefaultSearchProvider.ExclusiveTags.Add(SmartMaterialGraph.OutputOnlyTag);

            SchematicGraphEditorWindow.CanvasLoaded += (window, canvas) =>
            {
                canvas.Q("smart-material-preview")?.RemoveFromHierarchy();
                if (window.SchematicGraph is SmartMaterialGraph graph)
                    canvas.Add(new SmartMaterialPreviewPanel(graph));
            };

            // An edit to a Smart Material: a new version, so the preview makes it again.
            var counts = new Dictionary<SmartMaterialGraph, int>();
            double next = 0;
            EditorApplication.update += () =>
            {
                if (EditorApplication.timeSinceStartup < next)
                    return;
                next = EditorApplication.timeSinceStartup + 0.25;
                foreach (var graph in Resources.FindObjectsOfTypeAll<SmartMaterialGraph>())
                {
                    int dirty = EditorUtility.GetDirtyCount(graph);
                    if (counts.TryGetValue(graph, out int seen) && seen != dirty)
                        graph.Touch();
                    counts[graph] = dirty;
                }
            };
        }

        #region Making

        [MenuItem("Assets/Create/Schematics/Smart Material", priority = 84)]
        private static void Create()
        {
            string folder = "Assets";
            var selected = Selection.activeObject != null ? AssetDatabase.GetAssetPath(Selection.activeObject) : null;
            if (!string.IsNullOrEmpty(selected))
                folder = AssetDatabase.IsValidFolder(selected) ? selected : Path.GetDirectoryName(selected).Replace('\\', '/');
            var graph = CreateIn(folder);
            Selection.activeObject = graph;
            EditorGUIUtility.PingObject(graph);
        }

        /// <summary>
        /// A new Smart Material in a folder, with a starter stack wired up: painted metal, worn through at the edges,
        /// with grime in the creases. How the Project Manager makes them.
        /// </summary>
        [SchematicTypeCreator(typeof(SmartMaterialGraph))]
        public static SmartMaterialGraph CreateIn(string folder)
        {
            EnsureFolder(folder);
            string path = AssetDatabase.GenerateUniqueAssetPath($"{folder}/New Smart Material.asset");
            var graph = ScriptableObject.CreateInstance<SmartMaterialGraph>();
            AssetDatabase.CreateAsset(graph, path);

            var paint = NewNode<FillMaterialNode>("Paint", new Vector2(-300, -160));
            var metal = NewNode<FillMaterialNode>("Bare Metal", new Vector2(-300, 40));
            var wear = NewNode<EdgeWearMaskNode>("Edge Wear", new Vector2(-300, 240));
            var worn = NewNode<LayerMaterialNode>("Worn Edges", new Vector2(0, -40));
            var dirt = NewNode<FillMaterialNode>("Dirt", new Vector2(0, 200));
            var grime = NewNode<GrimeMaskNode>("Grime", new Vector2(0, 400));
            var dirty = NewNode<LayerMaterialNode>("Grime Layer", new Vector2(300, 60));
            var output = NewNode<MaterialOutputNode>("Output", new Vector2(600, 0));
            if (paint == null || metal == null || wear == null || worn == null || dirt == null || grime == null || dirty == null || output == null)
                return graph;

            paint.Color = new Color(0.17f, 0.32f, 0.55f, 1f);
            paint.Roughness = 0.45f;
            metal.Color = new Color(0.62f, 0.62f, 0.6f, 1f);
            metal.Metallic = 1f;
            metal.Roughness = 0.3f;
            metal.Height = 0.45f;
            dirt.Color = new Color(0.16f, 0.13f, 0.1f, 1f);
            dirt.Roughness = 0.9f;
            dirt.Variation = 0.5f;
            dirty.Opacity = 0.8f;
            foreach (var node in new Node[] { paint, metal, wear, worn, dirt, grime, dirty, output })
                graph.AddNode(node);
            Wire(graph, paint, MaterialNode.MaterialPort, worn, "Base");
            Wire(graph, metal, MaterialNode.MaterialPort, worn, "Top");
            Wire(graph, wear, MaterialNode.MaskPort, worn, "Mask");
            Wire(graph, worn, MaterialNode.MaterialPort, dirty, "Base");
            Wire(graph, dirt, MaterialNode.MaterialPort, dirty, "Top");
            Wire(graph, grime, MaterialNode.MaskPort, dirty, "Mask");
            Wire(graph, dirty, MaterialNode.MaterialPort, output, MaterialNode.MaterialPort);
            EditorUtility.SetDirty(graph);
            AssetDatabase.SaveAssets();
            return graph;
        }

        private static void Wire(Graph graph, Node from, string fromPort, Node to, string toPort)
        {
            var a = from.GetPort(fromPort);
            var b = to.GetPort(toPort);
            if (a != null && b != null)
                graph.AddEdge(a, b);
        }

        private static T NewNode<T>(string name, Vector2 position) where T : Node
        {
            var node = NodeReflection.GetNodeType(typeof(T))?.CreateInstance() as T;
            if (node == null)
                return null;
            node.Name = name;
            node.Position = position;
            return node;
        }

        internal static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder))
                return;
            string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }

        #endregion

        #region Render pipelines

        internal enum Pipeline { BuiltIn, Universal, HighDefinition }

        internal static Pipeline CurrentPipeline
        {
            get
            {
                var asset = GraphicsSettings.currentRenderPipeline;
                if (asset == null)
                    return Pipeline.BuiltIn;
                string type = asset.GetType().Name;
                return type.Contains("HDRenderPipeline") ? Pipeline.HighDefinition : type.Contains("Universal") ? Pipeline.Universal : Pipeline.BuiltIn;
            }
        }

        /// <summary>Auto packing made definite for this project's pipeline.</summary>
        internal static MaterialPacking Resolve(MaterialPacking packing)
            => packing != MaterialPacking.Auto ? packing : CurrentPipeline == Pipeline.HighDefinition ? MaterialPacking.HDRPMaskMap : MaterialPacking.UnityMetallic;

        /// <summary>The render pipeline's default lit material (or the built-in one).</summary>
        internal static Material DefaultMaterial()
        {
            var pipeline = GraphicsSettings.currentRenderPipeline;
            var material = pipeline != null ? pipeline.defaultMaterial : null;
            return material != null ? material : AssetDatabase.GetBuiltinExtraResource<Material>("Default-Material.mat");
        }

        /// <summary>
        /// Puts baked textures (by their <see cref="BakedTexture.Suffix"/>) on a lit material for this project's
        /// pipeline. Maps the pipeline can't read (ORM in URP, say) are left off.
        /// </summary>
        internal static void Assign(Material material, IReadOnlyDictionary<string, Texture> textures)
        {
            Texture Get(string suffix) => textures.TryGetValue(suffix, out var t) ? t : null;
            var baseColor = Get(MaterialBaker.BaseColor);
            var normal = Get(MaterialBaker.Normal);
            switch (CurrentPipeline)
            {
                case Pipeline.HighDefinition:
                    Set(material, "_BaseColorMap", baseColor);
                    SetColor(material, "_BaseColor", Color.white);
                    Set(material, "_NormalMap", normal, "_NORMALMAP", "_NORMALMAP_TANGENT_SPACE");
                    var mask = Get(MaterialBaker.MaskMap);
                    Set(material, "_MaskMap", mask, "_MASKMAP");
                    if (mask != null)
                    {
                        SetFloat(material, "_MetallicRemapMin", 0f);
                        SetFloat(material, "_MetallicRemapMax", 1f);
                        SetFloat(material, "_SmoothnessRemapMin", 0f);
                        SetFloat(material, "_SmoothnessRemapMax", 1f);
                        SetFloat(material, "_AORemapMin", 0f);
                        SetFloat(material, "_AORemapMax", 1f);
                    }
                    break;
                case Pipeline.Universal:
                    Set(material, "_BaseMap", baseColor);
                    Set(material, "_MainTex", baseColor);
                    SetColor(material, "_BaseColor", Color.white);
                    Set(material, "_BumpMap", normal, "_NORMALMAP");
                    SetFloat(material, "_BumpScale", 1f);
                    var metallic = Get(MaterialBaker.Metallic);
                    Set(material, "_MetallicGlossMap", metallic, "_METALLICSPECGLOSSMAP");
                    if (metallic != null)
                    {
                        SetFloat(material, "_Smoothness", 1f);
                        SetFloat(material, "_SmoothnessTextureChannel", 0f);
                    }
                    Set(material, "_OcclusionMap", Get(MaterialBaker.Occlusion), "_OCCLUSIONMAP");
                    SetFloat(material, "_OcclusionStrength", 1f);
                    break;
                default:
                    Set(material, "_MainTex", baseColor);
                    SetColor(material, "_Color", Color.white);
                    Set(material, "_BumpMap", normal, "_NORMALMAP");
                    var gloss = Get(MaterialBaker.Metallic);
                    Set(material, "_MetallicGlossMap", gloss, "_METALLICGLOSSMAP");
                    if (gloss != null)
                        SetFloat(material, "_GlossMapScale", 1f);
                    Set(material, "_OcclusionMap", Get(MaterialBaker.Occlusion));
                    SetFloat(material, "_OcclusionStrength", 1f);
                    break;
            }
        }

        private static void Set(Material material, string property, Texture texture, params string[] keywords)
        {
            if (!material.HasProperty(property))
                return;
            material.SetTexture(property, texture);
            foreach (var keyword in keywords)
            {
                if (texture != null)
                    material.EnableKeyword(keyword);
                else
                    material.DisableKeyword(keyword);
            }
        }

        private static void SetFloat(Material material, string property, float value)
        {
            if (material.HasProperty(property))
                material.SetFloat(property, value);
        }

        private static void SetColor(Material material, string property, Color value)
        {
            if (material.HasProperty(property))
                material.SetColor(property, value);
        }

        #endregion

        #region Baking

        /// <summary>
        /// Makes a Smart Material at its Output's Resolution and saves it next to the graph: a PNG per texture
        /// ("Name_BaseColor.png" and so on, imported as colour, normal map or data) and a material using them (made
        /// once, then updated in place, so what uses it picks the new textures up). Returns the material.
        /// </summary>
        public static async Task<Material> Bake(SmartMaterialGraph graph)
        {
            var output = graph != null ? graph.Output : null;
            if (output == null)
                return null;
            var plan = MaterialCompiler.Compile(graph);
            var packing = Resolve(output.Packing);
            float depth = output.HeightDepth, distance = output.OcclusionDistance;
            bool ao = output.AmbientOcclusion, writeHeight = output.WriteHeight;
            int padding = Mathf.Max(0, output.Padding * plan.Width / 1024);
            List<BakedTexture> baked = null;
            List<string> warnings = null;
            try
            {
                EditorUtility.DisplayProgressBar("Baking Smart Material", $"Making {graph.name} at {plan.Width}×{plan.Height}...", 0.3f);
                await Task.Run(() =>
                {
                    var result = plan.Run();
                    warnings = result.Warnings;
                    baked = MaterialBaker.Bake(result, packing, depth, ao, distance, Mathf.Max(padding, 2), writeHeight);
                });

                EditorUtility.DisplayProgressBar("Baking Smart Material", "Saving textures...", 0.8f);
                string folder = Path.GetDirectoryName(AssetDatabase.GetAssetPath(graph)).Replace('\\', '/');
                var textures = new Dictionary<string, Texture>();
                foreach (var texture in baked)
                {
                    string path = $"{folder}/{graph.name}_{texture.Suffix}.png";
                    textures[texture.Suffix] = WritePng(path, texture, plan.Width);
                }

                string materialPath = $"{folder}/{graph.name}.mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                if (material == null)
                {
                    material = new Material(DefaultMaterial()) { name = graph.name };
                    AssetDatabase.CreateAsset(material, materialPath);
                }
                Assign(material, textures);
                EditorUtility.SetDirty(material);
                AssetDatabase.SaveAssets();
                if (warnings != null)
                    foreach (var warning in warnings)
                        Debug.LogWarning($"[Smart Material {graph.name}] {warning}", graph);
                return material;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        // Writes a texture as a PNG and imports it as what it is (colour, normal map or data).
        private static Texture2D WritePng(string path, BakedTexture baked, int maxSize)
        {
            var texture = new Texture2D(baked.Width, baked.Height, TextureFormat.RGBA32, false, !baked.IsColor);
            texture.SetPixels32(baked.Pixels);
            texture.Apply(false);
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            if (AssetImporter.GetAtPath(path) is TextureImporter importer)
            {
                importer.textureType = baked.IsNormalMap ? TextureImporterType.NormalMap : TextureImporterType.Default;
                importer.sRGBTexture = baked.IsColor;
                importer.maxTextureSize = Mathf.Max(importer.maxTextureSize, maxSize);
                importer.alphaSource = baked.IsColor || baked.IsNormalMap ? TextureImporterAlphaSource.None : TextureImporterAlphaSource.FromInput;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        #endregion

        #region The mesh it's shown on

        private static Mesh _sphere;

        /// <summary>
        /// The Unity mesh to show a Smart Material on: its Output's Mesh (a Mesh Graph is made first), or a sphere for
        /// a flat material. <paramref name="release"/> frees what was made for it (null: nothing to free).
        /// </summary>
        internal static async Task<(Mesh mesh, System.Action release)> PreviewMesh(SmartMaterialGraph graph)
        {
            var source = graph != null && graph.Output != null ? graph.Output.Mesh : null;
            switch (source)
            {
                case MeshGraph meshGraph:
                {
                    var generated = await MeshGraphGenerator.Generate(meshGraph);
                    if (generated == null || generated.Levels.Length == 0)
                        return (Sphere(), null);
                    return (generated.Levels[0], () => MeshGraphGenerator.Release(generated));
                }
                case Mesh mesh:
                    return (mesh, null);
                case GameObject or Component:
                {
                    var made = MeshModelNode.Read(source, true).ToMesh(source.name);
                    made.hideFlags = HideFlags.HideAndDontSave;
                    return (made, () => Object.DestroyImmediate(made));
                }
                default:
                    return (Sphere(), null);
            }
        }

        private static Mesh Sphere()
        {
            if (_sphere != null)
                return _sphere;
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            _sphere = go.GetComponent<MeshFilter>().sharedMesh;
            Object.DestroyImmediate(go);
            return _sphere;
        }

        #endregion
    }
}
