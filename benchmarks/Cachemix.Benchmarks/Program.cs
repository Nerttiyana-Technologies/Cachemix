using System.Reflection;
using BenchmarkDotNet.Running;

// Entry point for the Cachemix overhead benchmarks.
//
// Run every benchmark:        dotnet run -c Release
// Run a single class/filter:  dotnet run -c Release -- --filter *MemoryCacheOverhead*
BenchmarkSwitcher.FromAssembly(Assembly.GetExecutingAssembly()).Run(args);
