using BlueGraph;
using BlueGraph.Editor;
using Remedy.Schematics;

namespace Remedy.Materials
{
    /// <summary>Smart Material math nodes (Add, Multiply, Screen, Mix...) drawn like every math node: a square with its icon.</summary>
    [CustomNodeView(typeof(MaterialMathNode))]
    public class MaterialMathNodeView : SquareIconNodeView
    { }
}
