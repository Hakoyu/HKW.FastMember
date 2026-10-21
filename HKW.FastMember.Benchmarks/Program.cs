using BenchmarkDotNet.Running;

namespace HKW.FastMember.Benchmarks;

internal class Program
{
    private static void Main()
    {
        BenchmarkRunner.Run<PropertyReadBenchmarks>();
        BenchmarkRunner.Run<PropertyWriteBenchmarks>();
        BenchmarkRunner.Run<DynamicReadBenchmarks>();
        BenchmarkRunner.Run<DynamicWriteBenchmarks>();
        BenchmarkRunner.Run<AccessorCreationBenchmarks>();
        BenchmarkRunner.Run<ObjectReaderBenchmarks>();
        BenchmarkRunner.Run<ConcurrentAccessBenchmarks>();
    }
}
