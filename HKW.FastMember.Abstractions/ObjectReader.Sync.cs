namespace HKW.FastMember.Abstractions;

public abstract partial class ObjectReader
{
    private sealed class SyncReader<T>(
        IEnumerable<T> source,
        TypeAccessor accessor,
        string[] members
    ) : Reader<T>(accessor, members)
    {
        private IEnumerator<T>? _enumerator;
        protected override T Current => _enumerator!.Current;

        protected override ValueTask<bool> MoveNextAsync()
        {
            _enumerator ??= source.GetEnumerator();
            return new ValueTask<bool>(_enumerator.MoveNext());
        }

        protected override ValueTask DisposeSourceAsync()
        {
            Interlocked.Exchange(ref _enumerator, null)?.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
