using System;

namespace Remedy.Framework
{
    // A custom attribute to specify the type of the ValueOutput port
    [AttributeUsage(AttributeTargets.Field)]
    public class OutputTypeAttribute : Attribute
    {
        public Type OutputType { get; }

        public OutputTypeAttribute(Type outputType)
        {
            OutputType = outputType;
        }
    }
}