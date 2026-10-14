// project armada

#pragma warning disable 0414

using BlueGraph;
using System;
using UnityEngine;

namespace Remedy.Schematics
{
    [Serializable]
    public class Cast<T> : SchematicGraphNode
    {
        [Input("Value")]
        public object Value;

        [Output("Casted")]
        public T CastedValue;

        public override object OnRequestValue(Port port)
        {
            // Converts what it can (a component to its GameObject or another component on it, numbers to numbers...);
            // anything else gives nothing rather than throwing.
            var value = GetInputValue<object>("Value", null);
            return PortValues.TryFromObject(value, out T casted) ? casted : default(T);
        }
    }
}