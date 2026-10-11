using System.Collections.Generic;

namespace REDox.Benchmarks.Data;

[DataSource("external/simdjson-data/jsonexamples/citm_catalog.json")]
public sealed class CitmCatalog
{
    public sealed class Root
    {
        public Dictionary<int, string>? areaNames { get; set; }

        public Dictionary<int, string>? audienceSubCategoryNames { get; set; }

        public Dictionary<int, string>? blockNames { get; set; }

        public Dictionary<int, string>? seatCategoryNames { get; set; }

        public Dictionary<int, string>? subTopicNames { get; set; }

        public Dictionary<int, string>? subjectNames { get; set; }

        public Dictionary<int, string>? topicNames { get; set; }

        public Dictionary<int, int[]>? topicSubTopics { get; set; }

        public Dictionary<string, string>? venueNames { get; set; }

        public Dictionary<int, Event>? events { get; set; }

        public Performance[]? performances { get; set; }
    }

    public class Event
    {
        public int id { get; set; }

        public string? name { get; set; }

        public string? description { get; set; }

        public string? subtitle { get; set; }

        public string? logo { get; set; }

        public int? subjectCode { get; set; }

        public int[]? topicIds { get; set; }

        public int[]? subTopicIds { get; set; }
    }

    public class Performance
    {
        public int id { get; set; }

        public int eventId { get; set; }

        public string? name { get; set; }

        public string? description { get; set; }

        public string? logo { get; set; }

        public Price[]? prices { get; set; }

        public SeatCategory[]? seatCategories { get; set; }

        public long start { get; set; }

        public string? seatMapImage { get; set; }

        public string? venueCode { get; set; }
    }

    public class Price
    {
        public int amount { get; set; }

        public int audienceSubCategoryId { get; set; }

        public int seatCategoryId { get; set; }
    }

    public class SeatCategory
    {
        public int seatCategoryId { get; set; }

        public Area[]? areas { get; set; }
    }

    public class Area
    {
        public int areaId { get; set; }

        public int[]? blockIds { get; set; }
    }
}