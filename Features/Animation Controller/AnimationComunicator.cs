using System;
using System.Collections.Generic;
using UnityEngine;

namespace Remedy.Animation
{
    /// <summary>
    /// Drives the Animator on its GameObject from Signals: each entry of its Properties maps a Signal to an Animator
    /// parameter (trigger, bool, float, or a Vector2 split into floats).
    /// </summary>
    [SchematicBehaviorPath("Animation/Animation Communicator")]
    public class AnimationComunicator : SchematicBehavior
    {
        /// <summary>The Signal-to-parameter mappings, assigned per object.</summary>
        public readonly VariableHandle<AnimationComunicatorProperties> Properties = (AnimationComunicatorProperties)null;

        protected override void OnStart(BehaviorInstance instance)
        {
            var properties = Properties.Value;
            var animator = instance.GetComponent<Animator>();
            if (properties == null || animator == null)
                return;

            foreach (var entry in properties.Triggers)
            {
                if (entry.ParameterName == 0) continue;
                int parameter = entry.ParameterName;
                // Triggers fire on any send, whatever the Signal carries.
                entry.OnEvent?.Subscribe(instance, () =>
                {
                    animator.ResetTrigger(parameter);
                    animator.SetTrigger(parameter);
                });
            }

            foreach (var entry in properties.BooleanValues)
            {
                if (entry.ParameterName == 0) continue;
                int parameter = entry.ParameterName;
                entry.OnEvent?.Subscribe(instance, (bool value) => animator.SetBool(parameter, value));
            }

            foreach (var entry in properties.FloatValues)
            {
                if (entry.ParameterName == 0) continue;
                int parameter = entry.ParameterName;
                entry.OnEvent?.Subscribe(instance, (float value) => animator.SetFloat(parameter, value));
            }

            foreach (var entry in properties.Vector2Values)
            {
                var mapping = entry;
                mapping.OnEvent?.Subscribe(instance, (Vector2 value) =>
                {
                    if (mapping.XParameter != 0) animator.SetFloat(mapping.XParameter, value.x);
                    if (mapping.YParameter != 0) animator.SetFloat(mapping.YParameter, value.y);
                    if (mapping.MagnitudeParameter != 0) animator.SetFloat(mapping.MagnitudeParameter, value.magnitude);
                });
            }
        }
    }
}
