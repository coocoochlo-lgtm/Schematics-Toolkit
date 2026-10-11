using UnityEngine;

namespace Remedy.Materials
{
    /// <summary>
    /// Drives material properties from Signals: each entry of its Properties maps a Signal to a shader property.
    /// Uses the Renderer's shared material unless the Properties name one.
    /// </summary>
    [SchematicBehaviorPath("Rendering/Material Communicator")]
    public class MaterialComunicator : SchematicBehavior
    {
        /// <summary>The Signal-to-shader-property mappings, assigned per object.</summary>
        public readonly VariableHandle<MaterialComunicatorProperties> Properties = (MaterialComunicatorProperties)null;

        protected override void OnStart()
        {
            var instance = Self;
            var properties = Properties.Value;
            if (properties == null)
                return;

            var material = properties.Material;
            if (material == null)
            {
                var renderer = instance.GetComponent<Renderer>();
                material = renderer != null ? renderer.sharedMaterial : null;
            }
            if (material == null)
                return;

            foreach (var entry in properties.FloatValues)
            {
                if (string.IsNullOrEmpty(entry.ParameterName)) continue;
                var parameter = entry.ParameterName;
                entry.OnEvent?.Subscribe(instance, (float value) => material.SetFloat(parameter, value));
            }

            foreach (var entry in properties.Vector2Values)
            {
                var mapping = entry;
                mapping.OnEvent?.Subscribe(instance, (Vector2 value) =>
                {
                    if (!string.IsNullOrEmpty(mapping.XParameter)) material.SetFloat(mapping.XParameter, value.x);
                    if (!string.IsNullOrEmpty(mapping.YParameter)) material.SetFloat(mapping.YParameter, value.y);
                    if (!string.IsNullOrEmpty(mapping.MagnitudeParameter)) material.SetFloat(mapping.MagnitudeParameter, value.magnitude);
                });
            }
        }
    }
}
