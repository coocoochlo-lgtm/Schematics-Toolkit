using System.Collections.Generic;

namespace REDox.Benchmarks.Data;

[DataSource("external/simdjson-data/jsonexamples/mesh.json")]
public class Mesh
{
    public sealed class Root
    {
        public MeshBatch[]? batches { get; set; }

        public Dictionary<string, object>? morphTargets { get; set; }

        public double[]? positions { get; set; }
        public double[]? tex0 { get; set; }

        public uint[]? colors { get; set; }

        public double[][]? influences { get; set; }

        public double[]? normals { get; set; }
        public int[]? indices { get; set; }
    }

    public sealed class MeshBatch
    {
        public int[]? indexRange { get; set; }
        public int[]? vertexRange { get; set; }
        public int[]? usedBones { get; set; }
    }
}