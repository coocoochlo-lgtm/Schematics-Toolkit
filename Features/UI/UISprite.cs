using Remedy.Framework;
using Remedy.Schematics;
using UnityEngine;

namespace Remedy.UI
{
    /// <summary>
    /// A textured quad on the UI layer. Create one in code with <see cref="New"/>.
    /// Its methods act on the selected instance, as returned by <see cref="New"/> or <c>GetSchematicBehavior&lt;UISprite&gt;()</c>.
    /// </summary>
    [SchematicBehaviorPath("UI/UI Sprite")]
    public class UISprite : UIComponent
    {
        /// <summary>
        /// Creates a quad GameObject with a UI Sprite attached, returned with that sprite selected.
        /// </summary>
        public static UISprite New(string name, Transform parent = null, int layer = 0)
        {
            GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = name;
            quad.transform.localPosition = Vector3.zero;
            quad.transform.localScale = Vector3.one;

            var renderer = quad.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit"))
                {
                    color = Color.white
                };
            }

            if (parent != null) quad.transform.parent = parent;

            // Adding the Schematic Object spawns it, so the sprite spawns as soon as it is attached.
            var schematicObject = quad.AddComponent<SchematicObject>();
            var instance = schematicObject.AddBehavior<UISprite>();
            var sprite = (UISprite)instance.Behavior;
            sprite.Layer[instance] = layer;
            sprite.Select(instance);
            return sprite;
        }

        /// <summary>
        /// Sets the Material of the selected Sprite.
        /// </summary>
        public void SetMaterial(Material material)
        {
            var renderer = Selected?.GetComponent<MeshRenderer>();
            if (renderer != null)
                renderer.sharedMaterial = material;
        }

        /// <summary>
        /// Sets the Main Texture of the selected Sprite's Material.
        /// </summary>
        public void SetTexture(Texture texture)
        {
            var renderer = Selected?.GetComponent<MeshRenderer>();
            if (renderer != null && renderer.sharedMaterial != null)
                renderer.sharedMaterial.SetTexture("_BaseMap", texture);
        }

        /// <summary>
        /// Extracts the Texture from a Sprite and assigns it to the selected UI Sprite.
        /// </summary>
        public void SetSprite(Sprite sprite)
        {
            SetTexture(sprite.ToTexture());
        }
    }
}
