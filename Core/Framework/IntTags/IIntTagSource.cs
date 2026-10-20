using System.Collections.Generic;

namespace Remedy.Framework
{
    /// <summary>
    /// A component that gives its GameObject Int Tags (<see cref="IntTagged"/>, SchematicObject). The registry asks
    /// these when it rebuilds itself in Edit Mode, and when one source drops a tag that another source on the same
    /// object still wants.
    /// </summary>
    public interface IIntTagSource
    {
        /// <summary>The tags this component gives its GameObject.</summary>
        IReadOnlyList<IntTag> Tags { get; }
    }
}
