namespace REDox.Benchmarks.Data;

[DataSource("external/simdjson-data/jsonexamples/apache_builds.json")]
public sealed class ApacheBuilds
{
    public sealed class Root
    {
        public AssignedLabel[]? assignedLabels { get; set; }

        public string? mode { get; set; }

        public string? nodeDescription { get; set; }

        public string? nodeName { get; set; }

        public int numExecutors { get; set; }

        public string? description { get; set; }

        public Job[]? jobs { get; set; }

        public LoadStatistics? overallLoad { get; set; }

        public View? primaryView { get; set; }

        public bool quietingDown { get; set; }

        public int slaveAgentPort { get; set; }

        public LoadStatistics? unlabeledLoad { get; set; }

        public bool useCrumbs { get; set; }

        public bool useSecurity { get; set; }

        public View[]? views { get; set; }
    }

    public class Job
    {
        public string? name { get; set; }

        public string? url { get; set; }

        public string? color { get; set; }
    }

    public class View
    {
        public string? name { get; set; }

        public string? url { get; set; }
    }

    public class LoadStatistics
    {
    }

    public class AssignedLabel
    {
    }
}