using System.Collections.Generic;

namespace REDox.Benchmarks.Data;

[DataSource("external/simdjson-data/jsonexamples/update-center.json")]
public class UpdateCenterTest
{
    public class Root
    {
        public string? connectionCheckUrl { get; set; }
        public Core? core { get; set; }
        public string? id { get; set; }
        public Dictionary<string, Plugin>? plugins { get; set; }
        public Signature? signature { get; set; }
        public string? updateCenterVersion { get; set; }
    }

    public class Core
    {
        public string? buildDate { get; set; }
        public string? name { get; set; }
        public string? sha1 { get; set; }
        public string? url { get; set; }
        public string? version { get; set; }
    }

    public class Plugin
    {
        public string? buildDate { get; set; }
        public Dependency[]? dependencies { get; set; }
        public Developer[]? developers { get; set; }
        public string? excerpt { get; set; }
        public string? gav { get; set; }
        public string[]? labels { get; set; }
        public string? name { get; set; }
        public string? previousTimestamp { get; set; }
        public string? previousVersion { get; set; }
        public string? releaseTimestamp { get; set; }
        public string? requiredCore { get; set; }
        public string? scm { get; set; }
        public string? sha1 { get; set; }
        public string? title { get; set; }
        public string? url { get; set; }
        public string? version { get; set; }
        public string? wiki { get; set; }
        public string? compatibleSinceVersion { get; set; }
    }

    public class Dependency
    {
        public string? name { get; set; }
        public bool optional { get; set; }
        public string? version { get; set; }
    }

    public class Developer
    {
        public string? developerId { get; set; }
        public string? email { get; set; }
        public string? name { get; set; }
    }

    public class Signature
    {
        public string[]? certificates { get; set; }

        public string? correct_digest { get; set; }
        public string? correct_signature { get; set; }

        public string? digest { get; set; }
        public string? signature { get; set; }
    }
}