using BenchmarkDotNet.Attributes;

namespace HKW.FastMember.Benchmarks;

[MemoryDiagnoser]
public class ConcurrentAccessBenchmarks
{
    private const int OperationCount = 1_024;
    private readonly TypeAccessor _accessor = TypeAccessor.Create(typeof(BenchmarkModel));
    private readonly BenchmarkModel[] _models = Enumerable
        .Range(0, OperationCount)
        .Select(i => new BenchmarkModel { Value = i })
        .ToArray();
    private readonly int[] _results = new int[OperationCount];

    [Benchmark(Baseline = true, OperationsPerInvoke = OperationCount)]
    public int DirectPropertyReads()
    {
        Parallel.For(0, OperationCount, i => _results[i] = _models[i].Value);
        return _results[^1];
    }

    [Benchmark(OperationsPerInvoke = OperationCount)]
    public int TypeAccessorReads()
    {
        Parallel.For(
            0,
            OperationCount,
            i => _results[i] = (int)_accessor[_models[i], nameof(BenchmarkModel.Value)]
        );
        return _results[^1];
    }
}
