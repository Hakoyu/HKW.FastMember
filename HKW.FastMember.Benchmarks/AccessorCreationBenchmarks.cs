using System.Reflection;
using System.Reflection.Emit;
using BenchmarkDotNet.Attributes;

namespace HKW.FastMember.Benchmarks;

[MemoryDiagnoser]
public class AccessorCreationBenchmarks
{
    private static readonly ModuleBuilder Module = AssemblyBuilder
        .DefineDynamicAssembly(
            new AssemblyName("AccessorCreationBenchmarks"),
            AssemblyBuilderAccess.Run
        )
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
    public Abstractions.TypeAccessor FirstIlAccessorCreation() => TypeAccessor.Create(_uncachedType);

    //[Benchmark]
    //public TypeAccessor FirstExpressionAccessorCreation() =>
    //    ExpressionTypeAccessor.Create(_uncachedType);
}
