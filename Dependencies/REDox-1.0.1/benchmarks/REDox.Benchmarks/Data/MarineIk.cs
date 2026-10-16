namespace REDox.Benchmarks.Data;

[DataSource("external/simdjson-data/jsonexamples/marine_ik.json")]
public class MarineIk
{
    public class Root
    {
        public MarineImage[]? images { get; set; }
        public MarineGeometry[]? geometries { get; set; }
        public MarineTexture[]? textures { get; set; }
        public MarineMetadata? metadata { get; set; }
        public MarineMaterial[]? materials { get; set; }
        public MarineSceneObject? @object { get; set; }
        public MarineTopAnimation[]? animations { get; set; }
    }

    public class MarineImage
    {
        public string? url { get; set; }
        public string? uuid { get; set; }
        public string? name { get; set; }
    }

    public class MarineTexture
    {
        public int[]? repeat { get; set; }
        public int[]? wrap { get; set; }
        public int anisotropy { get; set; }
        public string? image { get; set; }
        public string? name { get; set; }
        public int mapping { get; set; }
        public int minFilter { get; set; }
        public string? uuid { get; set; }
        public int magFilter { get; set; }
    }

    public class MarineMetadata
    {
        public string? sourceFile { get; set; }
        public string? generator { get; set; }
        public string? type { get; set; }
        public double version { get; set; }
    }

    public class MarineMaterial
    {
        public int vertexColors { get; set; }
        public string? name { get; set; }
        public string? type { get; set; }
        public string? uuid { get; set; }
        public string? blending { get; set; }
        public string? map { get; set; }
        public bool transparent { get; set; }
        public bool depthTest { get; set; }
        public int color { get; set; }
        public int shininess { get; set; }
        public int emissive { get; set; }
        public bool depthWrite { get; set; }
        public int specular { get; set; }
    }

    public class MarineGeometry
    {
        public string? type { get; set; }
        public string? uuid { get; set; }
        public MarineGeometryData? data { get; set; }
    }

    public class MarineGeometryData
    {
        public double[][]? uvs { get; set; }
        public MarineLegacyAnimationClip[]? animations { get; set; }
        public double[]? vertices { get; set; }
        public MarineGeometryDataMetadata? metadata { get; set; }
        public string? name { get; set; }
        public double[]? skinWeights { get; set; }
        public int[]? skinIndices { get; set; }
        public int influencesPerVertex { get; set; }
        public double[]? normals { get; set; }
        public MarineBone[]? bones { get; set; }
        public int[]? faces { get; set; }
    }

    public class MarineGeometryDataMetadata
    {
        public int uvs { get; set; }
        public int version { get; set; }
        public int faces { get; set; }
        public string? generator { get; set; }
        public int normals { get; set; }
        public int bones { get; set; }
        public int vertices { get; set; }
    }

    public class MarineBone
    {
        public int parent { get; set; }
        public double[]? pos { get; set; }
        public double[]? rotq { get; set; }
        public double[]? scl { get; set; }
        public string? name { get; set; }
    }

    public class MarineLegacyAnimationClip
    {
        public MarineLegacyHierarchyNode[]? hierarchy { get; set; }
        public double length { get; set; }
        public int fps { get; set; }
        public string? name { get; set; }
    }

    public class MarineLegacyHierarchyNode
    {
        public int parent { get; set; }
        public MarineLegacyKeyframe[]? keys { get; set; }
    }

    public class MarineLegacyKeyframe
    {
        public double[]? pos { get; set; }
        public double time { get; set; }
        public double[]? scl { get; set; }
        public double[]? rot { get; set; }
    }

    public class MarineSceneObject
    {
        public MarineSceneChild[]? children { get; set; }
        public string? type { get; set; }
        public double[]? matrix { get; set; }
        public string? uuid { get; set; }
    }

    public class MarineSceneChild
    {
        public string? name { get; set; }
        public string? uuid { get; set; }
        public double[]? matrix { get; set; }
        public bool visible { get; set; }
        public string? type { get; set; }
        public string? material { get; set; }
        public bool castShadow { get; set; }
        public bool receiveShadow { get; set; }
        public string? geometry { get; set; }
    }

    public class MarineTopAnimation
    {
        public int fps { get; set; }
        public string? name { get; set; }
    }
}