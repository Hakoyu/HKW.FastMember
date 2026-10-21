using System.Dynamic;

namespace HKW.FastMemberTests;

public class MemberModel
{
    private string? _privateProperty;
    private int _privateField;
    private string? _writeOnlyValue;

    public string? Name { get; set; }
    public int Number;
    public static string? StaticProperty { get; set; }
    public static int StaticField;
    public string ReadOnlyProperty { get; } = "read-only";
    public readonly int ReadOnlyField = 42;

    public string? WriteOnlyProperty
    {
        set => _writeOnlyValue = value;
    }

    public string this[int index]
    {
        get => $"item-{index}";
        set => IndexedValue = value;
    }

    public string? IndexedValue { get; private set; }

    private string? PrivateProperty
    {
        get => _privateProperty;
        set => _privateProperty = value;
    }

    private int PrivateField
    {
        get => _privateField;
        set => _privateField = value;
    }

    public string? GetPrivateProperty() => _privateProperty;

    public int GetPrivateField() => _privateField;

    public string? GetWriteOnlyValue() => _writeOnlyValue;
}

public class BaseModel
{
    public string? BaseValue { get; set; }
}

public sealed class DerivedModel : BaseModel
{
    public int DerivedValue { get; set; }
}

public interface IBaseContract
{
    string? Name { get; set; }
}

public interface IDerivedContract : IBaseContract
{
    int Number { get; set; }
}

public sealed class InterfaceModel : IDerivedContract
{
    public string? Name { get; set; }
    public int Number { get; set; }
}

public sealed class NullableModel
{
    public int? NullableNumber { get; set; }
    public string? NullableText { get; set; }
}

public sealed class DictionaryFirstModel
{
    public int Value { get; set; }
}

public struct StructModel
{
    public int Property { get; set; }
    public string? Field;
}

public sealed class OrdinalModel
{
    [HKW.FastMember.Ordinal(2)]
    public string? Last { get; set; }

    [HKW.FastMember.Ordinal(0)]
    public int First;

    [HKW.FastMember.Ordinal(1)]
    public string? Middle { get; set; }
}

public sealed class PartiallyOrdinalModel
{
    [HKW.FastMember.Ordinal(10)]
    public int Last { get; set; }

    public string? First { get; set; }
    public int? Middle { get; set; }
}

public sealed class MixedOrdinalModel
{
    [HKW.FastMember.Ordinal(1)]
    public string? Alpha { get; set; }

    public string? DefaultOrdinal { get; set; }

    [HKW.FastMember.Ordinal(1)]
    public string? Omega { get; set; }
}

public class HiddenBaseModel
{
    public virtual object? Value { get; set; }
}

public sealed class HiddenDerivedModel : HiddenBaseModel
{
    public new string? Value { get; set; }
}

public interface ILeftContract
{
    string? Shared { get; set; }
}

public interface IRightContract
{
    string? Shared { get; set; }
}

public interface ICombinedContract : ILeftContract, IRightContract
{
    bool Enabled { get; set; }
}

public sealed class RefReturnModel
{
    private int _value;

    public ref int Value => ref _value;

    public ref readonly int ReadOnlyValue => ref _value;
}

public sealed class ThrowingAccessorModel
{
    public string ThrowingGetter => throw new InvalidOperationException("getter failed");

    public string ThrowingSetter
    {
        set => throw new InvalidOperationException("setter failed");
    }
}

public static class DynamicModelFactory
{
    public static ExpandoObject Create(string name = "value", int number = 42)
    {
        var model = new ExpandoObject();
        var values = (IDictionary<string, object?>)model;
        values[name] = number;
        return model;
    }
}

public sealed class ReadOnlyDynamicModel : DynamicObject
{
    public override bool TryGetMember(GetMemberBinder binder, out object? result)
    {
        if (binder.Name == "Value")
        {
            result = 42;
            return true;
        }

        result = null;
        return false;
    }
}

public sealed class TrackingAsyncEnumerable<T> : IAsyncEnumerable<T>
{
    private readonly IReadOnlyList<T> _items;
    private readonly Exception? _moveNextException;
    private readonly bool _completeAsynchronously;

    public TrackingAsyncEnumerable(
        IEnumerable<T> items,
        Exception? moveNextException = null,
        bool completeAsynchronously = false
    )
    {
        _items = items.ToArray();
        _moveNextException = moveNextException;
        _completeAsynchronously = completeAsynchronously;
    }

    public CancellationToken EnumeratorCancellationToken { get; private set; }
    public int MoveNextCount { get; private set; }
    public int DisposeCount { get; private set; }

    public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
    {
        EnumeratorCancellationToken = cancellationToken;
        return new Enumerator(this);
    }

    private sealed class Enumerator : IAsyncEnumerator<T>
    {
        private readonly TrackingAsyncEnumerable<T> _owner;
        private int _index = -1;

        public Enumerator(TrackingAsyncEnumerable<T> owner) => _owner = owner;

        public T Current { get; private set; } = default!;

        public ValueTask<bool> MoveNextAsync()
        {
            if (_owner._completeAsynchronously)
                return CompleteAsync();

            try
            {
                return ValueTask.FromResult(MoveNext());
            }
            catch (Exception exception)
            {
                return ValueTask.FromException<bool>(exception);
            }
        }

        private async ValueTask<bool> CompleteAsync()
        {
            await Task.Yield();
            return MoveNext();
        }

        private bool MoveNext()
        {
            _owner.MoveNextCount++;
            _owner.EnumeratorCancellationToken.ThrowIfCancellationRequested();
            if (_owner._moveNextException is not null)
                throw _owner._moveNextException;

            _index++;
            if (_index >= _owner._items.Count)
                return false;

            Current = _owner._items[_index];
            return true;
        }

        public ValueTask DisposeAsync()
        {
            _owner.DisposeCount++;
            return ValueTask.CompletedTask;
        }
    }
}
