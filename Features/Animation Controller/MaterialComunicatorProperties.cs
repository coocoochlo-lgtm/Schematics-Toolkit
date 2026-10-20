using System;
using System.Collections.Generic;
using UnityEngine;

namespace Remedy.Materials
{
    /// <summary>
    /// The Signal-to-shader-property mappings of a <see cref="MaterialComunicator"/>.
    /// </summary>
    [CreateAssetMenu(fileName = "Material Communicator Properties", menuName = "Remedy Toolkit/Rendering/Material Communicator Properties")]
    public class MaterialComunicatorProperties : ScriptableObject
    {
        [Tooltip("The material to drive. Leave empty to use the Renderer's shared material.")]
        public Material Material;
        public List<FloatEvent> FloatValues = new();
        public List<Vector2Event> Vector2Values = new();

        [Serializable]
        public class FloatEvent
        {
            public SignalData OnEvent;
            //[ShaderParam(ShaderPropertyType.Float)]
            public string ParameterName;
        }
        [Serializable]
        public class Vector2Event
        {
            public SignalData OnEvent;
            //[ShaderParam(ShaderPropertyType.Float)]
            [Tooltip("The Parameter that is set to the X axis value of the Vector2 passed from the Event")]
            public string XParameter;
            //[ShaderParam(ShaderPropertyType.Float)]
            [Tooltip("The Parameter that is set to the Y axis value of the Vector2 passed from the Event")]
            public string YParameter;
            //[ShaderParam(ShaderPropertyType.Float)]
            [Tooltip("The Parameter that is set to the Magnitude value of the Vector2 passed from the Event")]
            public string MagnitudeParameter;
            //[ShaderParam(ShaderPropertyType.Vector)]
            public string Value;
        }
    }
}
