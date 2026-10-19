using Remedy.Framework;
using System.Linq;

namespace Unity.VisualScripting
{
    [Descriptor(typeof(Unit))]
    public class ReflectiveEventUnitDescriptor<T> : UnitDescriptor<ReflectiveEventUnit<T>> where T : ReflectiveEventUnit<T>
    {
        public ReflectiveEventUnitDescriptor(GraphInput unit) : base(default) { }

        protected override void DefinedPort(IUnitPort port, UnitPortDescription description)
        {
            base.DefinedPort(port, description);

            var definition = unit.graph.validPortDefinitions.OfType<IUnitInputPortDefinition>().SingleOrDefault(d => d.key == port.key);

            if (definition != null)
            {
                description.label = definition.Label();
                description.summary = definition.summary;

                if (definition.hideLabel)
                {
                    description.showLabel = false;
                }
            }
        }
    }
}
