using System;
using System.Collections.Generic;
using UnityEngine;

namespace Remedy.Animation
{
    /// <summary>
    /// The Signal-to-Animator-parameter mappings of an <see cref="AnimationComunicator"/>.
    /// </summary>
    [CreateAssetMenu(fileName = "Animation Communicator Properties", menuName = "Remedy Toolkit/Animation/Animation Communicator Properties")]
    public class AnimationComunicatorProperties : ScriptableObject
    {
        public List<TriggerEvent> Triggers = new();
        public List<BooleanEvent> BooleanValues = new();
        public List<FloatEvent> FloatValues = new();
        public List<Vector2Event> Vector2Values = new();

        [Serializable]
        public class TriggerEvent
        {
            public SignalData OnEvent;
            //[AnimatorParam(AnimatorControllerParameterType.Trigger)]
            public int ParameterName;
        }
        [Serializable]
        public class BooleanEvent
        {
            public SignalData OnEvent;
            //[AnimatorParam(AnimatorControllerParameterType.Bool)]
            public int ParameterName;
        }
        [Serializable]
        public class FloatEvent
        {
            public SignalData OnEvent;
            //[AnimatorParam(AnimatorControllerParameterType.Float)]
            public int ParameterName;
        }
        [Serializable]
        public class Vector2Event
        {
            public SignalData OnEvent;
            //[AnimatorParam(AnimatorControllerParameterType.Float)]
            [Tooltip("The Parameter that is set to the X axis value of the Vector2 passed from the Event")]
            public int XParameter;
            //[AnimatorParam(AnimatorControllerParameterType.Float)]
            [Tooltip("The Parameter that is set to the Y axis value of the Vector2 passed from the Event")]
            public int YParameter;
            //[AnimatorParam(AnimatorControllerParameterType.Float)]
            [Tooltip("The Parameter that is set to the Magnitude value of the Vector2 passed from the Event")]
            public int MagnitudeParameter;
        }
    }
}
