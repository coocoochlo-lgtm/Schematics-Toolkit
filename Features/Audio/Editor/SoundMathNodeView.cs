using BlueGraph;
using BlueGraph.Editor;
using Remedy.Schematics;

namespace Remedy.Audio
{
    /// <summary>Sound Cue math nodes (Add, Multiply, Sine Wave...) drawn like every math node: a square with its icon.</summary>
    [CustomNodeView(typeof(SoundMathOpNode))]
    public class SoundMathNodeView : SquareIconNodeView
    { }
}
