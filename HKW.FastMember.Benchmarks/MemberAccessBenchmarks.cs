using System.Dynamic;
using System.Linq.Expressions;
using System.Reflection;
using BenchmarkDotNet.Attributes;

namespace HKW.FastMember.Benchmarks;

public abstract class PropertyAccessBenchmarks
{
    protected const string MemberName = nameof(BenchmarkModel.Value);
    protected readonly BenchmarkModel Model = new() { Value = 42 };
    protected readonly TypeAccessor Accessor = TypeAccessor.Create(typeof(BenchmarkModel));
    protected readonly TypeAccessor ExpressionAccessor =
        ExpressionTypeAccessor.Create(typeof(BenchmarkModel));
    protected readonly PropertyInfo Property = typeof(BenchmarkModel).GetProperty(MemberName)!;
    protected ObjectAccessor ObjectAccessor = null!;

    [GlobalSetup]
    public void Setup()
    {
        ObjectAccessor = ObjectAccessor.Create(Model);

        if (
            (int)ExpressionAccessor[Model, MemberName] != Model.Value
            || !ExpressionAccessor.TryGetValue(Model, MemberName, out var value)
            || (int)value != Model.Value
            || ExpressionAccessor.TryGetValue(Model, "Missing", out _)
        )
        {
            throw new InvalidOperationException("Expression accessor getter validation failed.");
        }

        ExpressionAccessor[Model, MemberName] = 43;
        if (
            Model.Value != 43
            || !ExpressionAccessor.TrySetValue(Model, MemberName, 42)
            || Model.Value != 42
            || ExpressionAccessor.TrySetValue(Model, "Missing", 0)
        )
        {
            throw new InvalidOperationException("Expression accessor setter validation failed.");
        }
    }

    protected static Func<BenchmarkModel, int> CreateGetter()
    {
        var model = Expression.Parameter(typeof(BenchmarkModel), "model");
        return Expression
            .Lambda<Func<BenchmarkModel, int>>(Expression.Property(model, MemberName), model)
            .Compile();
    }

    protected static Action<BenchmarkModel, int> CreateSetter()
    {
        var model = Expression.Parameter(typeof(BenchmarkModel), "model");
        var value = Expression.Parameter(typeof(int), "value");
        return Expression
            .Lambda<Action<BenchmarkModel, int>>(
                Expression.Assign(Expression.Property(model, MemberName), value),
                model,
                value
            )
            .Compile();
    }
}

[MemoryDiagnoser]
public class PropertyReadBenchmarks : PropertyAccessBenchmarks
{
    private static readonly Func<BenchmarkModel, int> CompiledGetter = CreateGetter();

    [Benchmark(Baseline = true)]
    public int DirectProperty() => Model.Value;

    [Benchmark]
    public int TypeAccessorRead() => (int)Accessor[Model, MemberName];

    [Benchmark]
    public int ObjectAccessorRead() => (int)ObjectAccessor[MemberName];

    [Benchmark]
    public int ExpressionTypeAccessorRead() => (int)ExpressionAccessor[Model, MemberName];

    [Benchmark]
    public int ReflectionRead() => (int)Property.GetValue(Model)!;

    [Benchmark]
    public int TypedCompiledExpressionRead() => CompiledGetter(Model);
}

[MemoryDiagnoser]
public class PropertyWriteBenchmarks : PropertyAccessBenchmarks
{
    private static readonly Action<BenchmarkModel, int> CompiledSetter = CreateSetter();

    [Benchmark(Baseline = true)]
    public void DirectProperty() => Model.Value = 43;

    [Benchmark]
    public void TypeAccessorWrite() => Accessor[Model, MemberName] = 43;

    [Benchmark]
    public void ObjectAccessorWrite() => ObjectAccessor[MemberName] = 43;

    [Benchmark]
    public void ExpressionTypeAccessorWrite() => ExpressionAccessor[Model, MemberName] = 43;

    [Benchmark]
    public void ReflectionWrite() => Property.SetValue(Model, 43);

    [Benchmark]
    public void TypedCompiledExpressionWrite() => CompiledSetter(Model, 43);
}

public abstract class DynamicAccessBenchmarks
{
    protected const string MemberName = "Value";
    protected dynamic Dynamic = null!;
    protected ObjectAccessor Accessor = null!;

    [GlobalSetup]
    public void Setup()
    {
        Dynamic = new ExpandoObject();
        Dynamic.Value = 42;
        Accessor = ObjectAccessor.Create(Dynamic);
    }
}

[MemoryDiagnoser]
public class DynamicReadBenchmarks : DynamicAccessBenchmarks
{
    [Benchmark(Baseline = true)]
    public object DirectDynamic() => Dynamic.Value;

    [Benchmark]
    public object ObjectAccessorRead() => Accessor[MemberName];
}

[MemoryDiagnoser]
public class DynamicWriteBenchmarks : DynamicAccessBenchmarks
{
    [Benchmark(Baseline = true)]
    public void DirectDynamic() => Dynamic.Value = 43;

    [Benchmark]
    public void ObjectAccessorWrite() => Accessor[MemberName] = 43;
}
