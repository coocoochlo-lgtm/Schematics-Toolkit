//using SaintsField;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Remedy.Framework
{
    public class ScriptableGrid : ScriptableObject
    {
        public ScriptableGridNode[,] Content;

        private ScriptableGridNode[] _nodes;
        /// <summary>
        /// The <see cref="ScriptableGridNode">Node</see>'s of the <see cref="ScriptableGrid">Grid</see> as a Flattened List
        /// </summary>
        public ScriptableGridNode[] Nodes => _nodes ??= Flatten();

        [SerializeField]
        private List<ScriptableGridNodeWrapper> _gridNodes;

        public ScriptableGridNode this[int x, int y] => Content[x, y];

        public int Width => Content.GetLength(0);
        public int Height => Content.GetLength(1);

        private Dictionary<int, ScriptableGridNode[]> _tempArrays = new()
    {
        {1, new ScriptableGridNode[1]},
        {2, new ScriptableGridNode[2]},
        {3, new ScriptableGridNode[3]},
        {4, new ScriptableGridNode[4]}
    };

        private void OnValidate()
        {
            _gridNodes.Clear();
            foreach (var node in Nodes)
            {
                _gridNodes.Add(new(node));
            }
        }

        public virtual void SetupGrid(int width, int height)
        {
            Content = new ScriptableGridNode[width, height];

            for (int i = 0; i < height; i++)
            {
                for (int j = 0; j < width; j++)
                {
                    var node = ScriptableObject.CreateInstance<ScriptableGridNode>();
                    node.SetupNode(this, j, i);
                    Content[j, i] = node;
                }
            }
        }

        public bool ContainsPosition(int x, int y, params TileFlag[] flags)
        {
            return x < Width && y < Height && (this[x, y].Empty ^ !flags.Contains(TileFlag.IsEmpty)) && (!this[x, y].Empty ^ !flags.Contains(TileFlag.IsNotEmpty));
        }

        public ScriptableGridNode[] GetNodesAdjacentTo(ScriptableGridNode node, bool wrap)
        {
            if (node == null) return new ScriptableGridNode[0];
            return GetPositionAdjacentNodes(node.X, node.Y, wrap);
        }

        public ScriptableGridNode[] GetPositionAdjacentNodes(int x, int y, bool wrap, params TileFlag[] flags)
        {
            if (this.ContainsPosition(x, y, flags) || wrap)
            {
                int nodeCount = wrap ? 4 : x > 0 ? x < Width - 1 ? y > 0 ? y < Height - 1 ? 4 : 3 : 2 : 1 : 0;

                var nodes = _tempArrays[nodeCount];

                if (x > 0)
                    nodes[0] = this[x - 1, y];
                else if (wrap)
                    nodes[0] = this[Width - 1, y];

                if (x < Width - 1)
                    nodes[1] = this[x + 1, y];
                else if (wrap)
                    nodes[1] = this[0, y];

                if (y > 0)
                    nodes[2] = this[x, y - 1];
                else if (wrap)
                    nodes[2] = this[x, Height - 1];

                if (y < Height - 1)
                    nodes[3] = this[x, y + 1];
                else if (wrap)
                    nodes[3] = this[x, 0];

                return nodes;
            }
            return null;
        }

        public ScriptableGridNode[] GetNodesInRangeOfPosition(int x, int y, int range, bool wrap)
        {
            HashSet<ScriptableGridNode> nodes = new()
        {
            this[x, y]
        };

            int lastNodeIndex = 0;
            int currentNodeIndex = 0;

            for (int i = 0; i < range; i++)
            {
                lastNodeIndex = i * 4;
                while (currentNodeIndex >= lastNodeIndex && currentNodeIndex < nodes.Count)
                {
                    var node = nodes.ElementAt(currentNodeIndex);

                    var adjacentNodes = GetPositionAdjacentNodes(node.X, node.Y, wrap);
                    if (adjacentNodes != null && adjacentNodes.Length > 0)
                    {
                        nodes.UnionWith(adjacentNodes);
                    }

                    currentNodeIndex++;
                }
            }

            return nodes.ToArray();
        }



        public ScriptableGridNode[] GetNodesInRangeOf(ScriptableGridNode node, int range, bool wrap)
        {
            return GetNodesInRangeOfPosition(node.X, node.Y, range, wrap);
        }

        public ScriptableGridNode[] Row(int y)
        {
            var nodes = new ScriptableGridNode[Width];
            for (int i = 0; i < Width; i++)
            {
                nodes[i] = this[i, y];
            }

            return nodes;
        }

        public ScriptableGridNode[] Column(int x)
        {
            var nodes = new ScriptableGridNode[Height];

            for (int i = 0; i < Height; i++)
            {
                nodes[i] = this[x, i];
            }

            return nodes;
        }

        private ScriptableGridNode[] Flatten()
        {
            var flattenedList = new ScriptableGridNode[Width * Height];

            for (int i = 0; i < (Width * Height); i++)
            {
                flattenedList[i] = this[i % Width, i / Width];
            }

            return flattenedList;
        }
        public IEnumerable<ScriptableGridNode> GetRangeIntersection(ScriptableGridNode[] firstRange, ScriptableGridNode[] secondRange)
        {
            var asHashSet = Enumerable.ToHashSet(firstRange);
            asHashSet.UnionWith(secondRange);
            return firstRange;
        }

        public ScriptableGridNode GetClosestNodeInRange(ScriptableGridNode[] range, int x, int y)
        {
            var closestNode = range[0];
            var thisPosition = new Vector2(x, y);
            float lastDistance = 999999999;

            foreach (var node in range)
            {
                float currentDistance = Vector2.Distance(thisPosition, new Vector2(node.X, node.Y));
                if (currentDistance < lastDistance)
                {
                    lastDistance = currentDistance;
                    closestNode = node;
                }
            }

            return closestNode;
        }

        [Serializable]
        public class ScriptableGridNodeWrapper
        {
            //[Expandable]
            [SerializeField]
            private ScriptableGridNode _node;

            public ScriptableGridNodeWrapper(ScriptableGridNode node)
            {
                this._node = node;
            }
        }
    }

    public class ScriptableGrid<T> : ScriptableGrid where T : ScriptableGridNode
    {
        public override void SetupGrid(int width, int height)
        {
            Content = new ScriptableGridNode[width, height];

            for (int i = 0; i < height; i++)
            {
                for (int j = 0; j < width; j++)
                {
                    var node = ScriptableObject.CreateInstance<T>();
                    node.SetupNode(this, j, i);
                    Content[j, i] = node;
                }
            }
        }
    }

    public enum TileFlag
    {
        IsEmpty,
        IsNotEmpty
    }
}


