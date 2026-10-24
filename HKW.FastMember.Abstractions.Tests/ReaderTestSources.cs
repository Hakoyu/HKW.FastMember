using System.Collections;
using HKW.FastMember.Abstractions;

namespace HKW.FastMember.Abstractions.Tests;

internal static class ReaderFactory
{
    public static ObjectReader Sync<T>(IEnumerable<T> source, params string[] members) where T : class => ObjectReader.Create(source, ReaderAccessor.Instance, members);
    public static ObjectReader Async<T>(IAsyncEnumerable<T> source, params string[] members) where T : class => ObjectReader.Create(source, ReaderAccessor.Instance, members);
    public static ObjectReader Async<T>(IAsyncEnumerable<T> source, CancellationToken token, params string[] members) where T : class => ObjectReader.Create(source, ReaderAccessor.Instance, token, members);
}

internal sealed class ReaderRow
{
    public bool Boolean { get; set; } = true;
    public byte Byte { get; set; } = 7;
    public byte[] Bytes { get; set; } = [1, 2, 3, 4];
    public char Character { get; set; } = 'x';
    public char[] Chars { get; set; } = ['a', 'b', 'c'];
    public DateTime Date { get; set; } = new(2026, 1, 2);
    public decimal Decimal { get; set; } = 1.25m;
    public double Double { get; set; } = 2.5;
    public float Float { get; set; } = 3.5f;
    public Guid Guid { get; set; } = Guid.Parse("12345678-1234-1234-1234-123456789abc");
    public int Id { get; set; } = 42;
    public long Long { get; set; } = 100L;
    public int? Nullable { get; set; }
    public short Short { get; set; } = 5;
    public string? Text { get; set; } = "hello";
    public Exception? GetterException { get; set; }
}

internal sealed class ReaderAccessor : TypeAccessor<ReaderRow>
{
    internal static ReaderAccessor Instance { get; } = new();
    private ReaderAccessor() : base([
        new("Boolean", typeof(bool), -1, true, true, "row.Boolean", typeof(bool), false),
        new("Byte", typeof(byte), -1, true, true, "row.Byte", typeof(byte), false),
        new("Bytes", typeof(byte[]), -1, true, true, "row.Bytes", typeof(byte[]), true),
        new("Character", typeof(char), -1, true, true, "row.Character", typeof(char), false),
        new("Chars", typeof(char[]), -1, true, true, "row.Chars", typeof(char[]), true),
        new("Date", typeof(DateTime), -1, true, true, "row.Date", typeof(DateTime), false),
        new("Decimal", typeof(decimal), -1, true, true, "row.Decimal", typeof(decimal), false),
        new("Double", typeof(double), -1, true, true, "row.Double", typeof(double), false),
        new("Float", typeof(float), -1, true, true, "row.Float", typeof(float), false),
        new("Guid", typeof(Guid), -1, true, true, "row.Guid", typeof(Guid), false),
        new("Id", typeof(int), 0, true, true, "row.Id", typeof(int), false),
        new("Long", typeof(long), -1, true, true, "row.Long", typeof(long), false),
        new("Nullable", typeof(int?), -1, true, true, "row.Nullable", typeof(int), true),
        new("Short", typeof(short), -1, true, true, "row.Short", typeof(short), false),
        new("Text", typeof(string), 1, true, true, "row.Text", typeof(string), true),
        new("Throwing", typeof(int), -1, true, false, "row.Throwing", typeof(int), false),
        new("WriteOnly", typeof(int), -1, false, true, "row.WriteOnly", typeof(int), false)
    ], false) { }

    protected override object? GetValueTypedCore(ReaderRow target, int index) => index switch
    {
        0 => target.Boolean, 1 => target.Byte, 2 => target.Bytes, 3 => target.Character,
        4 => target.Chars, 5 => target.Date, 6 => target.Decimal, 7 => target.Double,
        8 => target.Float, 9 => target.Guid, 10 => target.Id, 11 => target.Long,
        12 => target.Nullable, 13 => target.Short, 14 => target.Text,
        15 => target.GetterException is { } error ? throw error : target.Id,
        _ => throw new ArgumentOutOfRangeException(nameof(index))
    };
    protected override void SetValueTypedCore(ReaderRow target, int index, object? value) => throw new NotSupportedException();
}

internal sealed class ReaderFailure(string message) : Exception(message);

internal sealed class TrackingSource<T>(params T[] rows) : IEnumerable<T>
{
    public int Acquired, Moves, Currents, Disposals;
    public Exception? AcquireError, MoveError, CurrentError, DisposeError;
    public int FailAtMove = 1;
    public Action? OnMove;
    public IEnumerator<T> GetEnumerator()
    {
        Acquired++;
        if (AcquireError is not null) throw AcquireError;
        return new Cursor(this);
    }
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    private sealed class Cursor(TrackingSource<T> owner) : IEnumerator<T>
    {
        private int _index = -1;
        public T Current
        {
            get
            {
                owner.Currents++;
                if (owner.CurrentError is not null) throw owner.CurrentError;
                return owner.Row(_index);
            }
        }
        object? IEnumerator.Current => Current;
        public bool MoveNext()
        {
            owner.Moves++;
            owner.OnMove?.Invoke();
            if (owner.MoveError is not null && owner.Moves == owner.FailAtMove) throw owner.MoveError;
            return ++_index < owner.Count;
        }
        public void Reset() => throw new NotSupportedException();
        public void Dispose()
        {
            owner.Disposals++;
            if (owner.DisposeError is not null) throw owner.DisposeError;
        }
    }
    private int Count => rows.Length;
    private T Row(int index) => rows[index];
}

internal sealed class TrackingAsyncSource<T>(params T[] rows) : IAsyncEnumerable<T>
{
    public int Acquired, Moves, Currents, Disposals;
    public CancellationToken ReceivedToken;
    public Exception? AcquireError, MoveError, CurrentError, DisposeError;
    public int FailAtMove = 1;
    public TaskCompletionSource<bool>? MoveGate, MoveStarted, DisposeGate;
    public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
    {
        Acquired++;
        ReceivedToken = cancellationToken;
        if (AcquireError is not null) throw AcquireError;
        return new Cursor(this);
    }
    private sealed class Cursor(TrackingAsyncSource<T> owner) : IAsyncEnumerator<T>
    {
        private int _index = -1;
        public T Current
        {
            get
            {
                owner.Currents++;
                if (owner.CurrentError is not null) throw owner.CurrentError;
                return owner.Row(_index);
            }
        }
        public async ValueTask<bool> MoveNextAsync()
        {
            owner.Moves++;
            owner.MoveStarted?.TrySetResult(true);
            if (owner.MoveGate is { } gate) await gate.Task.ConfigureAwait(false);
            if (owner.MoveError is not null && owner.Moves == owner.FailAtMove) throw owner.MoveError;
            return ++_index < owner.Count;
        }
        public async ValueTask DisposeAsync()
        {
            owner.Disposals++;
            if (owner.DisposeGate is { } gate) await gate.Task.ConfigureAwait(false);
            if (owner.DisposeError is not null) throw owner.DisposeError;
        }
    }
    private int Count => rows.Length;
    private T Row(int index) => rows[index];
}
