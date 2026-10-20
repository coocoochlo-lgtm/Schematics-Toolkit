using System.Collections.Generic;
using REDox.Benchmarks.Data;
using REDox.MessagePack.Benchmarks;

#if DEBUG
new MessagePackDeserialize<Gsoc2018, Dictionary<int, Gsoc2018.GsocProject>>().Setup();
#else
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Running;

var config = DefaultConfig.Instance
    .WithOptions(ConfigOptions.JoinSummary);

var switcher = BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly);
switcher.Run(args, config);
#endif