using System;
using System.Collections.Generic;
using System.Linq;

namespace BlueGraph.Editor
{
    /// <summary>
    /// Default implementation of a SearchProvider for nodes. 
    /// 
    /// This provider provides all nodes found via NodeReflection.
    /// </summary>
    public class DefaultSearchProvider : ISearchProvider
    {
        public bool IsSupported(IGraph graph)
        {
            return true;
        }

        public IEnumerable<SearchResult> GetSearchResults(SearchFilter filter)
        {
            foreach (var entry in NodeReflection.GetNodeTypes())
            {
                var node = entry.Value;
                if (
                    IsCompatible(filter.SourcePort, node) && 
                    IsInSupportedTags(filter.IncludeTags, node.Tags)
                ) {
                    yield return new SearchResult
                    {
                        Name = node.Name,
                        Path = node.Path,
                        UserData = node,
                    };
                }
            }
        }

        public Node Instantiate(SearchResult result)
        {
            NodeReflectionData data = result.UserData as NodeReflectionData;
            return data.CreateInstance();
        }
        
        /// <summary>
        /// Returns true if the intersection between the tags and our allow
        /// list has more than one tag, OR if our allow list is empty.
        /// </summary>
        private bool IsInSupportedTags(IEnumerable<string> supported, IEnumerable<string> tags)
            => Allows(supported, tags);

        /// <summary>
        /// Tags whose nodes only belong in graphs that include them by name ([IncludeTags]): kept out of graphs with
        /// no include list (which otherwise offer every node). Sound Cue nodes, for example.
        /// </summary>
        public static readonly HashSet<string> ExclusiveTags = new HashSet<string>();

        /// <summary>
        /// Whether a node with <paramref name="tags"/> belongs in a graph including <paramref name="supported"/>: one
        /// tag in common, or (with no include list) any node without an exclusive tag.
        /// </summary>
        public static bool Allows(IEnumerable<string> supported, IEnumerable<string> tags)
        {
            tags ??= Enumerable.Empty<string>();
            if (tags.Contains(TagsAttribute.AnyGraph))
                return true;
            if (supported == null || !supported.Any())
                return !tags.Any(ExclusiveTags.Contains);
            return supported.Intersect(tags).Any();
        }

        private bool IsCompatible(Port sourcePort, NodeReflectionData node)
        {
            if (sourcePort == null)
            {
                return true;
            }

            if (sourcePort.Direction == PortDirection.Input)
            {
                return node.HasOutputOfType(sourcePort.Type);
            }

            return node.HasInputOfType(sourcePort.Type);
        }
    }
}
