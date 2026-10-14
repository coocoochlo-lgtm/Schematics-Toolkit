using System.Collections.Generic;
using System.Text.Json.Serialization;
using REDox.Benchmarks.Data;

namespace REDox.Json.Benchmarks;

[JsonSerializable(typeof(Twitter.Root), TypeInfoPropertyName = nameof(Twitter))]
[JsonSerializable(typeof(Canada.Root), TypeInfoPropertyName = nameof(Canada))]
[JsonSerializable(typeof(CitmCatalog.Root), TypeInfoPropertyName = nameof(CitmCatalog))]
[JsonSerializable(typeof(double[]), TypeInfoPropertyName = nameof(Numbers))]
[JsonSerializable(typeof(ApacheBuilds.Root), TypeInfoPropertyName = nameof(ApacheBuilds))]
[JsonSerializable(typeof(GitHubEvents.GitHubEvent[]), TypeInfoPropertyName = nameof(GitHubEvents))]
[JsonSerializable(typeof(Dictionary<int, Gsoc2018.GsocProject>), TypeInfoPropertyName = nameof(Gsoc2018))]
[JsonSerializable(typeof(UpdateCenterTest.Root), TypeInfoPropertyName = nameof(UpdateCenterTest))]
[JsonSerializable(typeof(Mesh.Root), TypeInfoPropertyName = nameof(Mesh))]
[JsonSerializable(typeof(MarineIk.Root), TypeInfoPropertyName = nameof(MarineIk))]
[JsonSerializable(typeof(Random.Root), TypeInfoPropertyName = nameof(Random))]
[JsonSerializable(typeof(Instruments.Root), TypeInfoPropertyName = nameof(Instruments))]
public partial class DataSerializerContext : JsonSerializerContext
{
}