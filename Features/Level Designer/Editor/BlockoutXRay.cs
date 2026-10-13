using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Overlays;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Remedy.LevelDesigner.Editor
{
    /// <summary>
    /// While an art design is the active scene and its blockout is loaded underneath, the blockout's own rendering
    /// is hidden in the Scene view and it's drawn instead as a see-through X-ray (on top of the art, fainter where
    /// art covers it), so art can be lined up with the blockout precisely. Blockout objects can't be picked in this
    /// mode, so clicks land on the art. Turning the overlay off hides the blockout completely.<br/>
    /// Only the Scene view is affected: the Game view and Play Mode never see the X-ray.
    /// </summary>
    [InitializeOnLoad]
    public static class BlockoutXRay
    {
        private struct Item
        {
            public Mesh Mesh;
            public int SubMeshCount;
            public Transform Transform;
            public Color Color;
        }

        private static readonly List<Item> _items = new();
        private static bool _itemsDirty = true;
        private static Scene _hiddenScene;
        private static MaterialPropertyBlock _block;
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int OpacityId = Shader.PropertyToID("_Opacity");
        private static readonly Color DefaultColor = new Color(0.3f, 0.75f, 1f, 1f);

        static BlockoutXRay()
        {
            RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
            LevelDesignerContext.ModeChanged += ApplyVisibility;
            EditorApplication.hierarchyChanged += () => _itemsDirty = true;
            EditorSceneManager.sceneClosing += OnSceneClosing;
            EditorApplication.delayCall += ApplyVisibility;
        }

        /// <summary>True while the X-ray is being drawn.</summary>
        public static bool IsActive =>
            LevelDesignerContext.Mode == LevelDesignerMode.ArtDesign && LevelDesignerSettings.XRayEnabled &&
            LevelDesignerContext.BlockoutSceneUnderArt.IsValid();

        // Hides (or shows again) the blockout's normal rendering and picking in the Scene view.
        private static void ApplyVisibility()
        {
            _itemsDirty = true;
            var visibility = SceneVisibilityManager.instance;

            // Whatever was hidden for an art design before, shown again (unless it's still the one under the art).
            var blockoutUnderArt = LevelDesignerContext.Mode == LevelDesignerMode.ArtDesign ? LevelDesignerContext.BlockoutSceneUnderArt : default;
            if (_hiddenScene.IsValid() && _hiddenScene.isLoaded && _hiddenScene != blockoutUnderArt)
                Restore(_hiddenScene);
            _hiddenScene = default;

            // A blockout being edited directly must always be visible and pickable (in case an earlier session left it hidden).
            if (LevelDesignerContext.Mode == LevelDesignerMode.Blockout)
            {
                var active = SceneManager.GetActiveScene();
                var roots = active.GetRootGameObjects();
                if (roots.Length > 0 && (visibility.IsHidden(roots[0]) || visibility.IsPickingDisabled(roots[0])))
                    Restore(active);
            }

            if (!blockoutUnderArt.IsValid() || !blockoutUnderArt.isLoaded)
            {
                SceneView.RepaintAll();
                return;
            }

            var blockoutRoots = blockoutUnderArt.GetRootGameObjects();
            visibility.Hide(blockoutRoots, true);
            visibility.DisablePicking(blockoutRoots, true);
            _hiddenScene = blockoutUnderArt;
            SceneView.RepaintAll();
        }

        private static void Restore(Scene scene)
        {
            var roots = scene.GetRootGameObjects();
            SceneVisibilityManager.instance.Show(roots, true);
            SceneVisibilityManager.instance.EnablePicking(roots, true);
        }

        // Unhide a blockout before it's closed, so it isn't still hidden the next time it's opened on its own.
        private static void OnSceneClosing(Scene scene, bool removingScene)
        {
            if (_hiddenScene.IsValid() && scene == _hiddenScene)
            {
                Restore(scene);
                _hiddenScene = default;
            }
        }

        private static void Collect()
        {
            _itemsDirty = false;
            _items.Clear();
            var scene = LevelDesignerContext.BlockoutSceneUnderArt;
            if (!scene.IsValid() || !scene.isLoaded)
                return;

            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var meshRenderer in root.GetComponentsInChildren<MeshRenderer>())
                {
                    if (!meshRenderer.enabled || !meshRenderer.gameObject.activeInHierarchy)
                        continue;
                    var filter = meshRenderer.GetComponent<MeshFilter>();
                    if (filter == null || filter.sharedMesh == null)
                        continue;
                    var shape = meshRenderer.GetComponent<BlockoutShape>();
                    _items.Add(new Item
                    {
                        Mesh = filter.sharedMesh,
                        SubMeshCount = filter.sharedMesh.subMeshCount,
                        Transform = meshRenderer.transform,
                        Color = shape != null ? shape.Color : DefaultColor,
                    });
                }
            }
        }

        // Queues the X-ray meshes for the Scene view camera only, just before it renders.
        private static void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (camera.cameraType != CameraType.SceneView || !IsActive)
                return;
            var material = LevelDesignerContext.XRayMaterial;
            if (material == null)
                return;
            if (_itemsDirty)
                Collect();

            _block ??= new MaterialPropertyBlock();
            bool categoryColors = LevelDesignerSettings.XRayCategoryColors;
            float opacity = LevelDesignerSettings.XRayOpacity;
            foreach (var item in _items)
            {
                if (item.Transform == null || item.Mesh == null)
                {
                    _itemsDirty = true;
                    continue;
                }
                _block.Clear();
                _block.SetColor(ColorId, categoryColors ? item.Color : DefaultColor);
                _block.SetFloat(OpacityId, opacity);
                var matrix = item.Transform.localToWorldMatrix;
                for (int sub = 0; sub < item.SubMeshCount; sub++)
                    Graphics.DrawMesh(item.Mesh, matrix, material, 0, camera, sub, _block, ShadowCastingMode.Off, false);
            }
        }
    }

    /// <summary>
    /// The art design panel in the Scene view: the blockout X-ray toggle and its opacity, or a button to bring the
    /// blockout in when the art design was opened on its own.
    /// </summary>
    [Overlay(typeof(SceneView), "Remedy.LevelDesigner.ArtDesign", "Art Design", true)]
    public class ArtDesignOverlay : Overlay, ITransientOverlay
    {
        public bool visible => LevelDesignerContext.Mode == LevelDesignerMode.ArtDesign;

        private VisualElement _root;
        private Toggle _toggle;
        private Slider _opacity;
        private Toggle _colors;

        public override VisualElement CreatePanelContent()
        {
            _root = new VisualElement { style = { minWidth = 200 } };
            Rebuild();
            _root.RegisterCallback<AttachToPanelEvent>(_ =>
            {
                LevelDesignerContext.ModeChanged += Rebuild;
                LevelDesignerSettings.Changed += Sync;
            });
            _root.RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                LevelDesignerContext.ModeChanged -= Rebuild;
                LevelDesignerSettings.Changed -= Sync;
            });
            return _root;
        }

        private void Rebuild()
        {
            if (_root == null)
                return;
            _root.Clear();
            _toggle = null;

            if (!LevelDesignerContext.BlockoutSceneUnderArt.IsValid())
            {
                _root.Add(new Label("The blockout isn't loaded."));
                _root.Add(new Button(LevelDesignerContext.LoadBlockoutUnderActiveArt) { text = "Load Blockout Underneath" });
                return;
            }

            _toggle = new Toggle("Blockout X-Ray") { tooltip = "Show the blockout through the art" };
            _toggle.RegisterValueChangedCallback(evt => LevelDesignerSettings.XRayEnabled = evt.newValue);
            _root.Add(_toggle);

            _opacity = new Slider("Opacity", 0.05f, 1f);
            _opacity.RegisterValueChangedCallback(evt => LevelDesignerSettings.XRayOpacity = evt.newValue);
            _root.Add(_opacity);

            _colors = new Toggle("Category Colours") { tooltip = "Tint each shape with its category colour" };
            _colors.RegisterValueChangedCallback(evt => LevelDesignerSettings.XRayCategoryColors = evt.newValue);
            _root.Add(_colors);
            Sync();
        }

        // Settings changed (here, in the window, or elsewhere): update the controls without rebuilding them, so a
        // slider being dragged keeps working.
        private void Sync()
        {
            if (_toggle == null)
                return;
            _toggle.SetValueWithoutNotify(LevelDesignerSettings.XRayEnabled);
            _opacity.SetValueWithoutNotify(LevelDesignerSettings.XRayOpacity);
            _colors.SetValueWithoutNotify(LevelDesignerSettings.XRayCategoryColors);
            _opacity.SetEnabled(LevelDesignerSettings.XRayEnabled);
            _colors.SetEnabled(LevelDesignerSettings.XRayEnabled);
        }
    }
}
