using BenchmarkDotNet.Attributes;

namespace HKW.FastMember.Benchmarks;

[MemoryDiagnoser]
public class ObjectReaderBenchmarks
{
    private readonly BenchmarkModel[] _rows = Enumerable
        .Range(0, 1_024)
        .Select(i => new BenchmarkModel { Value = i, Name = $"Item {i}" })
        .ToArray();

    [Benchmark(Baseline = true)]
    public int DirectLoop()
    {
        var total = 0;
        foreach (var row in _rows)
            total += row.Value;
        return total;
    }

    [Benchmark]
    public int ObjectReaderLoop()
    {
        using var reader = ObjectReader.Create(_rows, nameof(BenchmarkModel.Value));
        var total = 0;
        while (reader.Read())
            total += reader.GetInt32(0);
        return total;
    }
}
