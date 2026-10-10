using Remedy.Framework;
using BlueGraph;
using System;
using System.Linq;
using UnityEngine;

namespace Remedy.Schematics
{
    [Serializable]
    [Output("▶", typeof(ActionPort), enableCasting: false, Multiple = true)]
    public class SchematicEventNode : SchematicGraphNode
    {
        protected bool _blocked = false;


        public override object OnRequestValue(Port port)
        {
            return null;
        }

        public void Trigger(GameObject instance, params Union[] arguments)
        {
            // Copy the keys first: assigning while enumerating the dictionary would throw.
            int i = 0;
            foreach (var key in _cachedOutputsByName.Keys.ToArray())
            {
                if (arguments == null || i >= arguments.Length)
                    break;
                _cachedOutputsByName[key] = arguments[i];
                i++;
            }

            OnTrigger(instance);

            if(!_blocked)
                ProcessChildren(instance);
        }

        protected virtual void OnTrigger(GameObject instance, params Union[] arguments)
        { }

        public void ProcessChildren(GameObject instance, bool awaiting = false, bool parallel = true)
        {
            if (IsDirty)
                UpdateCaches();

            if (_cachedChildren.Count == 0) return;

                for (int i = 0; i < _cachedChildren.Count; i++)
                {
                    var child = _cachedChildren[i];
                    child.Trigger(instance, false);
                }
        }
    }
}
