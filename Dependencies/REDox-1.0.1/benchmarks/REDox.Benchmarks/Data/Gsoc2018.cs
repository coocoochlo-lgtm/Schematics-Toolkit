using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using MessagePack;
using Newtonsoft.Json;
using REDox.Serialization;

namespace REDox.Benchmarks.Data;

[DataSource("external/simdjson-data/jsonexamples/gsoc-2018.json")]
public class Gsoc2018
{
    [MessagePackObject]
    public class GsocProject
    {
        [Key("@context")]
        [JsonPropertyName("@context")]
        [JsonProperty("@context")]
        [DataProperty("@context")]
        [DataMember(Name = "@context")]
        public string? Context { get; set; }

        [Key("@type")]
        [JsonPropertyName("@type")]
        [JsonProperty("@type")]
        [DataProperty("@type")]
        [DataMember(Name = "@type")]
        public string? Type { get; set; }

        [Key("name")]
        [JsonPropertyName("name")]
        [JsonProperty("name")]
        [DataProperty("name")]
        [DataMember(Name = "name")]
        public string? Name { get; set; }

        [Key("description")]
        [JsonPropertyName("description")]
        [JsonProperty("description")]
        [DataProperty("description")]
        [DataMember(Name = "description")]
        public string? Description { get; set; }

        [Key("sponsor")]
        [JsonPropertyName("sponsor")]
        [JsonProperty("sponsor")]
        [DataProperty("sponsor")]
        [DataMember(Name = "sponsor")]
        public Sponsor? Sponsor { get; set; }

        [Key("author")]
        [JsonPropertyName("author")]
        [JsonProperty("author")]
        [DataProperty("author")]
        [DataMember(Name = "author")]
        public Author? Author { get; set; }
    }

    [MessagePackObject]
    public class Sponsor
    {
        [Key("@type")]
        [JsonPropertyName("@type")]
        [JsonProperty("@type")]
        [DataProperty("@type")]
        [DataMember(Name = "@type")]
        public string? Type { get; set; }

        [Key("name")]
        [JsonPropertyName("name")]
        [JsonProperty("name")]
        [DataProperty("name")]
        [DataMember(Name = "name")]
        public string? Name { get; set; }

        [Key("disambiguatingDescription")]
        [JsonPropertyName("disambiguatingDescription")]
        [JsonProperty("disambiguatingDescription")]
        [DataProperty("disambiguatingDescription")]
        [DataMember(Name = "disambiguatingDescription")]
        public string? DisambiguatingDescription { get; set; }

        [Key("description")]
        [JsonPropertyName("description")]
        [JsonProperty("description")]
        [DataProperty("description")]
        [DataMember(Name = "description")]
        public string? Description { get; set; }

        [Key("url")]
        [JsonPropertyName("url")]
        [JsonProperty("url")]
        [DataProperty("url")]
        [DataMember(Name = "url")]
        public string? Url { get; set; }

        [Key("logo")]
        [JsonPropertyName("logo")]
        [JsonProperty("logo")]
        [DataProperty("logo")]
        [DataMember(Name = "logo")]
        public string? Logo { get; set; }
    }

    [MessagePackObject]
    public class Author
    {
        [Key("@type")]
        [JsonPropertyName("@type")]
        [JsonProperty("@type")]
        [DataProperty("@type")]
        [DataMember(Name = "@type")]
        public string? Type { get; set; }

        [Key("name")]
        [JsonPropertyName("name")]
        [JsonProperty("name")]
        [DataProperty("name")]
        [DataMember(Name = "name")]
        public string? Name { get; set; }
    }
}