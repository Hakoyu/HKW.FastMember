using System.Reflection;
using System.Reflection.Emit;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;

namespace HKW.FastMember.Benchmarks;

[MemoryDiagnoser]
[SimpleJob(RunStrategy.ColdStart, launchCount: 1, warmupCount: 0, iterationCount: 10, invocationCount: 1)]
public class AccessorCreationBenchmarks
{
    private static readonly ModuleBuilder Module = AssemblyBuilder
        .DefineDynamicAssembly(new AssemblyName("AccessorCreationBenchmarks"), AssemblyBuilderAccess.Run)
        .DefineDynamicModule("Types");

    private Type _uncachedType = null!;
    private int _typeId;

    [IterationSetup]
    public void CreateUniqueType()
    {
        var builder = Module.DefineType($"BenchmarkType{_typeId++}", TypeAttributes.Public);
        builder.DefineField("Value", typeof(int), FieldAttributes.Public);
        _uncachedType = builder.CreateType()!;
    }

    [Benchmark]
    public TypeAccessor FirstAccessorCreation() => TypeAccessor.Create(_uncachedType);
}
