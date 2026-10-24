namespace HKW.FastMember.Abstractions;

public abstract partial class ObjectReader
{
    private sealed class AsyncReader<T>(
        IAsyncEnumerable<T> source,
        TypeAccessor accessor,
        CancellationToken cancellationToken,
        string[] members
    ) : Reader<T>(accessor, members)
    {
        private IAsyncEnumerator<T>? _enumerator;
        protected override T Current => _enumerator!.Current;

        protected override ValueTask<bool> MoveNextAsync()
        {
            _enumerator ??= source.GetAsyncEnumerator(cancellationToken);
            return _enumerator.MoveNextAsync();
        }

        protected override ValueTask DisposeSourceAsync() =>
            Interlocked.Exchange(ref _enumerator, null)?.DisposeAsync() ?? ValueTask.CompletedTask;
    }
}
