namespace REDox.Benchmarks.Data;

[DataSource("external/simdjson-data/jsonexamples/canada.json")]
public sealed class Canada
{
    public sealed class Root
    {
        public string? type { get; set; }

        public Feature[]? features { get; set; }
    }

    public sealed class Feature
    {
        public string? type { get; set; }

        public FeatureProperties? properties { get; set; }

        public Geometry? geometry { get; set; }
    }

    public sealed class FeatureProperties
    {
        public string? name { get; set; }
    }

    public sealed class Geometry
    {
        public string? type { get; set; }

        public double[][][]? coordinates { get; set; }
    }
}