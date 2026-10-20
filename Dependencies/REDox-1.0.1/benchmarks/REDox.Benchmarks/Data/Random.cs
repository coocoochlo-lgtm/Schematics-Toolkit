namespace REDox.Benchmarks.Data;

[DataSource("external/simdjson-data/jsonexamples/random.json")]
public class Random
{
    public sealed class Root
    {
        public int id { get; set; }
        public string? jsonrpc { get; set; }
        public int total { get; set; }
        public User2[]? result { get; set; }
    }

    public sealed class User2
    {
        public int id { get; set; }
        public string? avatar { get; set; }
        public int age { get; set; }
        public bool admin { get; set; }
        public string? name { get; set; }
        public string? company { get; set; }
        public string? phone { get; set; }
        public string? email { get; set; }

        // 例: "Mon, 05 Jan 1998 15:59:20 GMT" 
        public string? birthDate { get; set; }

        public Friend[]? friends { get; set; }
        public string? field { get; set; }
    }

    public sealed class Friend
    {
        public int id { get; set; }
        public string? name { get; set; }
        public string? phone { get; set; }
    }
}