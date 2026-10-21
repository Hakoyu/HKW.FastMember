using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;

namespace HKW.FastMember.Benchmarks;

internal class Program
{
    private static void Main()
    {
        var config = DefaultConfig.Instance.AddJob(
            Job.Default.WithWarmupCount(10).WithIterationCount(10)
        );
        BenchmarkRunner.Run<PropertyReadBenchmarks>(config);
        BenchmarkRunner.Run<PropertyWriteBenchmarks>(config);
        //BenchmarkRunner.Run<DynamicReadBenchmarks>(config);
        //BenchmarkRunner.Run<DynamicWriteBenchmarks>(config);
        BenchmarkRunner.Run<AccessorCreationBenchmarks>(config);
        //BenchmarkRunner.Run<ObjectReaderBenchmarks>(config);
        //BenchmarkRunner.Run<ConcurrentAccessBenchmarks>(config);
    }
}
