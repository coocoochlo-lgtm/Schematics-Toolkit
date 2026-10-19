using Unity.VisualScripting;
using UnityEngine;

namespace Remedy.Framework
{
    public class Cast<T> : Unit where T : MonoBehaviour
    {
        public ValueInput Object;
        public ValueOutput Result;
        protected override void Definition()
        {
            this.Object = ValueInput<Object>("Object");
            this.Result = ValueOutput("As " + typeof(T).Name, (flow) =>
            {
                var obj = flow.GetValue<Object>(Object);
                return obj.GetCachedComponent<T>();
            });
        }
    }
}