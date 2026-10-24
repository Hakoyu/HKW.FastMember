using System.Collections;
using System.Dynamic;
using System.Runtime.CompilerServices;
using HKW.FastMember;
using Contracts = HKW.FastMember.Abstractions;

var supportsDynamicCode = RuntimeFeature.IsDynamicCodeSupported;
var checkedFactories = 0;
try
{
    VerifyNull(() => TypeAccessor.Create(null!));
    VerifyNull(() => TypeAccessor.Create(null!, true));
    VerifyNull(() => ObjectAccessor.Create(null!));
    VerifyNull(() => ObjectAccessor.Create(null!, true));

    var row = new Row();
    var hidden = new HiddenRow();
    var privateSetter = new PrivateSetterRow();
    var rows = new TrackingRows();
    var expando = new ExpandoObject();
    ((IDictionary<string, object?>)expando)["Value"] = 1;
    var dynamicRow = new DynamicRow();

    // 这些直接调用刻意在调用方触发 IL3050/IL2026 诊断。
    Check("TypeAccessor/public", () => TypeAccessor.Create(typeof(Row)), row);
    Check("TypeAccessor/public-mode", () => TypeAccessor.Create(typeof(Row), false), row);
    Check("TypeAccessor/non-public-type", () => TypeAccessor.Create(typeof(HiddenRow)), hidden);
    Check("TypeAccessor/private-setter", () => TypeAccessor.Create(typeof(PrivateSetterRow), true), privateSetter);
    Check("TypeAccessor/expando", () => TypeAccessor.Create(typeof(ExpandoObject)), expando);
    Check("TypeAccessor/dynamic", () => TypeAccessor.Create(typeof(DynamicRow), true), dynamicRow);
    Check("ObjectAccessor/public", () => ObjectAccessor.Create(row), row);
    Check("ObjectAccessor/public-mode", () => ObjectAccessor.Create(row, false), row);
    Check("ObjectAccessor/non-public-type", () => ObjectAccessor.Create(hidden), hidden);
    Check("ObjectAccessor/private-setter", () => ObjectAccessor.Create(privateSetter, true), privateSetter);
    Check("ObjectAccessor/expando", () => ObjectAccessor.Create(expando), expando);
    Check("ObjectAccessor/expando-mode", () => ObjectAccessor.Create(expando, true), expando);
    Check("ObjectAccessor/dynamic", () => ObjectAccessor.Create(dynamicRow), dynamicRow);
    Check("ObjectAccessor/dynamic-mode", () => ObjectAccessor.Create(dynamicRow, true), dynamicRow);
    Check("ObjectReader/sync", () => ObjectReader.Create((IEnumerable<Row>)rows), null);
    await CheckAsyncReader("ObjectReader/async", () => ObjectReader.Create((IAsyncEnumerable<Row>)rows));
    await CheckAsyncReader("ObjectReader/async-token", () => ObjectReader.Create((IAsyncEnumerable<Row>)rows, CancellationToken.None));

    if (supportsDynamicCode)
    {
        Require(TypeHelpers.GetMembers<Row>().Any(member => member.Name == "Value"), "JIT metadata discovery failed.");
        Require(rows.EnumeratorsCreated == 3, "Each reader must create exactly one enumerator.");
        Require(Row.GetterCalls > 0, "JIT reads did not execute the getter.");
    }
    else
    {
        Require(rows.EnumeratorsCreated == 0, "AOT rejection must precede enumeration.");
        Require(Row.GetterCalls == 0, "AOT rejection must not execute user getters.");
    }

    Console.WriteLine($"PASS: {checkedFactories} factories; IsDynamicCodeSupported={supportsDynamicCode}; " +
        (supportsDynamicCode ? "JIT reads and writes verified." : "Native AOT early rejection verified (not AOT accessor support)."));
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception);
    return 1;
}

void Check(string name, Func<object> factory, object? target)
{
    checkedFactories++;
    if (!supportsDynamicCode)
    {
        VerifyRejection(name, factory);
        return;
    }

    switch (factory())
    {
        case Contracts.TypeAccessor accessor:
            accessor[target!, "Value"] = 42;
            Require(Equals(accessor[target!, "Value"], 42), name);
            break;
        case Contracts.ObjectAccessor accessor:
            accessor["Value"] = 42;
            Require(Equals(accessor["Value"], 42), name);
            break;
        case Contracts.ObjectReader reader:
            using (reader)
            {
                Require(reader.Read() && reader.GetInt32(0) == 1, name);
                Require(!reader.Read(), name);
            }
            break;
        default:
            throw new InvalidOperationException($"Unexpected factory result: {name}");
    }
}

async Task CheckAsyncReader(string name, Func<Contracts.ObjectReader> factory)
{
    checkedFactories++;
    if (!supportsDynamicCode)
    {
        VerifyRejection(name, factory);
        return;
    }

    await using var reader = factory();
    Require(await reader.ReadAsync() && reader.GetInt32(0) == 1, name);
    Require(!await reader.ReadAsync(), name);
}

static void VerifyRejection(string name, Func<object> factory)
{
    try
    {
        var result = factory();
        (result as IDisposable)?.Dispose();
    }
    catch (PlatformNotSupportedException exception)
    {
        Require(exception.Message.Contains("dynamic code", StringComparison.OrdinalIgnoreCase), name);
        Require(exception.Message.Contains("Native AOT", StringComparison.Ordinal), name);
        return;
    }
    throw new InvalidOperationException($"Expected early platform rejection: {name}");
}

static void VerifyNull(Action action)
{
    try
    {
        action();
    }
    catch (ArgumentNullException)
    {
        return;
    }
    throw new InvalidOperationException("Null factory arguments must retain ArgumentNullException.");
}

static void Require(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

public sealed class Row
{
    public static int GetterCalls;
    private int _value = 1;
    public int Value
    {
        get { GetterCalls++; return _value; }
        set { _value = value; }
    }
}

internal sealed class HiddenRow
{
    public int Value { get; set; }
}

public sealed class PrivateSetterRow
{
    public int Value { get; private set; }
}

public sealed class DynamicRow : DynamicObject
{
    private object? _value = 1;
    public override bool TryGetMember(GetMemberBinder binder, out object? result)
    {
        result = _value;
        return binder.Name == "Value";
    }

    public override bool TrySetMember(SetMemberBinder binder, object? value)
    {
        if (binder.Name != "Value")
            return false;
        _value = value;
        return true;
    }
}

internal sealed class TrackingRows : IEnumerable<Row>, IAsyncEnumerable<Row>
{
    public int EnumeratorsCreated { get; private set; }
    public IEnumerator<Row> GetEnumerator()
    {
        EnumeratorsCreated++;
        return ((IEnumerable<Row>)new[] { new Row() }).GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public IAsyncEnumerator<Row> GetAsyncEnumerator(CancellationToken cancellationToken = default)
    {
        EnumeratorsCreated++;
        return Enumerate(cancellationToken).GetAsyncEnumerator(cancellationToken);
    }

    private static async IAsyncEnumerable<Row> Enumerate([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
        yield return new Row();
    }
}
