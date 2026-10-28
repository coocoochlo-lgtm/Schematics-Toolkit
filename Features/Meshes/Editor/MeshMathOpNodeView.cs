using BlueGraph;
using BlueGraph.Editor;
using Remedy.Schematics;

namespace Remedy.Meshes
{
    /// <summary>Mesh Graph math nodes (Add, Multiply, Sin...) drawn like every math node: a square with its icon.</summary>
    [CustomNodeView(typeof(MeshMathOpNode))]
    public class MeshMathOpNodeView : SquareIconNodeView
    { }
}
