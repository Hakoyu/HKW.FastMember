using HKW.FastMember.Abstractions;

namespace HKW.FastMember.Abstractions.Tests;

[TestClass]
public sealed class ObjectReaderAsyncTests
{
    [TestMethod]
    public async Task AsyncCreationAndSchemaNeverEnumerateAndCreationTokenIsPassedUnchanged()
    {
        using var creation = new CancellationTokenSource();
        using var perCall = new CancellationTokenSource();
        var source = new TrackingAsyncSource<ReaderRow>(new ReaderRow());
        await using var reader = ReaderFactory.Async(source, creation.Token, "Nullable", "Id");
        Assert.AreEqual(2, reader.FieldCount);
        Assert.AreEqual(typeof(int), reader.GetFieldType(0));
        Assert.AreEqual(true, reader.GetColumnSchema()[0].AllowDBNull);
        using var schema = reader.GetSchemaTable();
        Assert.AreEqual(0, source.Acquired);
        Assert.AreEqual(0, source.Moves);
        Assert.IsTrue(await reader.ReadAsync(perCall.Token));
        Assert.AreEqual(creation.Token, source.ReceivedToken);
        Assert.AreEqual(42, reader.GetInt32(1));
        Assert.IsTrue(reader.HasRows);
        Assert.IsFalse(await reader.ReadAsync());
        Assert.IsTrue(reader.HasRows);
        Assert.IsFalse(await reader.ReadAsync());
        Assert.AreEqual(2, source.Moves);
        Assert.AreEqual(1, source.Disposals);
    }

    [TestMethod]
    public void AsyncCreateValidatesAllNullsColumnsAndCanceledCreationToken()
    {
        var source = new TrackingAsyncSource<ReaderRow>();
        Assert.ThrowsExactly<ArgumentNullException>(() => ReaderFactory.Async<ReaderRow>(null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => ReaderFactory.Async(source, null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => ReaderFactory.Async(source, new string[] { null! }));
        Assert.ThrowsExactly<ArgumentNullException>(() => ReaderFactory.Async(source, CancellationToken.None, null!));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => ReaderFactory.Async(source, "WriteOnly"));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => ReaderFactory.Async(source, "nope"));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var error = Assert.ThrowsExactly<OperationCanceledException>(() => ReaderFactory.Async(source, cancellation.Token));
        Assert.AreEqual(cancellation.Token, error.CancellationToken);
        Assert.AreEqual(0, source.Acquired);
    }

    [TestMethod]
    public async Task AsyncHasRowsPrefetchCacheSurvivesCanceledCallAndDoesNotExposeCurrent()
    {
        var source = new TrackingAsyncSource<ReaderRow>(new() { Id = 1 }, new() { Id = 2 });
        await using var reader = ReaderFactory.Async(source, "Id");
        Assert.IsTrue(reader.HasRows);
        Assert.IsTrue(reader.HasRows);
        Assert.AreEqual(1, source.Moves);
        Assert.ThrowsExactly<InvalidOperationException>(() => reader.GetValue(0));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => reader.ReadAsync(cancellation.Token));
        Assert.AreEqual(1, source.Moves);
        Assert.IsTrue(await reader.ReadAsync());
        Assert.AreEqual(1, reader.GetInt32(0));
        Assert.AreEqual(1, source.Moves);
        Assert.IsTrue(await reader.ReadAsync());
        Assert.AreEqual(2, reader.GetInt32(0));
        Assert.IsFalse(await reader.ReadAsync());
        Assert.IsTrue(reader.HasRows);
        Assert.AreEqual(1, source.Disposals);
    }

    [TestMethod]
    public async Task CanceledReadBeforeFirstMovementDoesNotAcquireEnumerator()
    {
        var source = new TrackingAsyncSource<ReaderRow>(new ReaderRow());
        await using var reader = ReaderFactory.Async(source);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => reader.ReadAsync(cancellation.Token));
        Assert.AreEqual(0, source.Acquired);
        Assert.IsTrue(await reader.ReadAsync());
        Assert.AreEqual(CancellationToken.None, source.ReceivedToken);
    }

    [TestMethod]
    public async Task RunningMoveIsAwaitedDespitePerCallCancellationAndOverlapsAreRejected()
    {
        using var cancellation = new CancellationTokenSource();
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var source = new TrackingAsyncSource<ReaderRow>(new ReaderRow()) { MoveGate = gate };
        await using var reader = ReaderFactory.Async(source, "Id");
        var pending = reader.ReadAsync(cancellation.Token);
        Assert.AreEqual(1, source.Moves);
        Assert.IsFalse(pending.IsCompleted);
        cancellation.Cancel();
        Assert.IsFalse(pending.IsCompleted);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => reader.ReadAsync());
        Assert.ThrowsExactly<InvalidOperationException>(() => reader.Read());
        Assert.ThrowsExactly<InvalidOperationException>(() => _ = reader.HasRows);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => reader.NextResultAsync());
        Assert.AreEqual(1, source.Moves);
        gate.SetResult(true);
        Assert.IsTrue(await pending);
        Assert.AreEqual(42, reader.GetInt32(0));
        Assert.AreEqual(1, source.Acquired);
        Assert.IsFalse(await reader.ReadAsync());
    }

    [TestMethod]
    public async Task AsyncEofWaitsForDisposalBeforeCompletingAndReleasesOnce()
    {
        var disposal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var source = new TrackingAsyncSource<ReaderRow>() { DisposeGate = disposal };
        var reader = ReaderFactory.Async(source);
        var pending = reader.ReadAsync();
        Assert.AreEqual(1, source.Disposals);
        Assert.IsFalse(pending.IsCompleted);
        disposal.SetResult(true);
        Assert.IsFalse(await pending);
        Assert.IsFalse(reader.HasRows);
        reader.Close();
        reader.Dispose();
        await reader.DisposeAsync();
        Assert.AreEqual(1, source.Disposals);
    }

    [TestMethod]
    public async Task AsyncDisposeWaitsForUserDisposalAndMixedRepeatedCallsAreIdempotent()
    {
        var disposal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var source = new TrackingAsyncSource<ReaderRow>(new ReaderRow()) { DisposeGate = disposal };
        var reader = ReaderFactory.Async(source);
        Assert.IsTrue(await reader.ReadAsync());
        var pending = reader.DisposeAsync().AsTask();
        Assert.IsTrue(reader.IsClosed);
        Assert.AreEqual(1, source.Disposals);
        Assert.IsFalse(pending.IsCompleted);
        disposal.SetResult(true);
        await pending;
        reader.Close();
        reader.Dispose();
        await reader.DisposeAsync();
        Assert.AreEqual(1, source.Disposals);
    }

    [TestMethod]
    public async Task AsyncNextResultEndsCachedResultAndDisposesWithoutFurtherMovement()
    {
        var source = new TrackingAsyncSource<ReaderRow>(new(), new());
        await using var reader = ReaderFactory.Async(source, "Id");
        Assert.IsTrue(reader.HasRows);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => reader.NextResultAsync(cancellation.Token));
        Assert.IsTrue(await reader.ReadAsync());
        Assert.IsFalse(await reader.NextResultAsync());
        Assert.IsFalse(reader.HasRows);
        Assert.IsFalse(reader.Read());
        Assert.IsFalse(await reader.ReadAsync());
        Assert.ThrowsExactly<InvalidOperationException>(() => reader.GetValue(0));
        Assert.AreEqual(1, source.Moves);
        Assert.AreEqual(1, source.Disposals);
    }

    [TestMethod]
    public async Task AsyncNoReadDisposeAndNextResultNeverAcquire()
    {
        var source = new TrackingAsyncSource<ReaderRow>(new ReaderRow());
        var reader = ReaderFactory.Async(source);
        await reader.DisposeAsync();
        reader.Close();
        Assert.AreEqual(0, source.Acquired);
        Assert.AreEqual(0, source.Disposals);
        var source2 = new TrackingAsyncSource<ReaderRow>(new ReaderRow());
        await using var reader2 = ReaderFactory.Async(source2);
        Assert.IsFalse(await reader2.NextResultAsync());
        Assert.IsFalse(reader2.HasRows);
        Assert.IsFalse(await reader2.ReadAsync());
        Assert.AreEqual(0, source2.Acquired);
    }

    [TestMethod]
    public async Task AsyncNullRowEndsReadingAndCleansUpWithoutMaskingInvalidRow()
    {
        var source = new TrackingAsyncSource<ReaderRow>(null!, new()) { DisposeError = new ReaderFailure("cleanup") };
        var reader = ReaderFactory.Async(source, "Id");
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => reader.ReadAsync());
        Assert.ThrowsExactly<InvalidOperationException>(() => reader.GetValue(0));
        Assert.IsFalse(await reader.ReadAsync());
        Assert.IsFalse(reader.HasRows);
        await reader.DisposeAsync();
        Assert.AreEqual(1, source.Disposals);
        Assert.AreEqual(1, source.Moves);
    }

    [TestMethod]
    public async Task AsyncMoveAndCurrentExceptionsAreOriginalAndTerminalDespiteCleanupFailure()
    {
        foreach (var currentFailure in new[] { false, true })
        {
            var error = new ReaderFailure("enumeration");
            var source = new TrackingAsyncSource<ReaderRow>(new ReaderRow()) { DisposeError = new ReaderFailure("cleanup") };
            if (currentFailure) source.CurrentError = error; else source.MoveError = error;
            var reader = ReaderFactory.Async(source);
            Assert.AreSame(error, await Assert.ThrowsExactlyAsync<ReaderFailure>(() => reader.ReadAsync()));
            Assert.IsFalse(await reader.ReadAsync());
            Assert.ThrowsExactly<InvalidOperationException>(() => reader.GetValue(0));
            await reader.DisposeAsync();
            reader.Dispose();
            Assert.AreEqual(1, source.Disposals);
            Assert.AreEqual(1, source.Moves);
        }
    }

    [TestMethod]
    public async Task AsyncAcquisitionExceptionDoesNotRetryOrDisposeMissingEnumerator()
    {
        var error = new ReaderFailure("acquire");
        var source = new TrackingAsyncSource<ReaderRow>() { AcquireError = error };
        await using var reader = ReaderFactory.Async(source);
        Assert.AreSame(error, await Assert.ThrowsExactlyAsync<ReaderFailure>(() => reader.ReadAsync()));
        Assert.IsFalse(await reader.ReadAsync());
        Assert.AreEqual(1, source.Acquired);
        Assert.AreEqual(0, source.Disposals);
    }

    [TestMethod]
    public async Task EnumeratorCancellationIsTerminalAndPreservesOriginalToken()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var error = new OperationCanceledException(cancellation.Token);
        var source = new TrackingAsyncSource<ReaderRow>(new ReaderRow()) { MoveError = error };
        await using var reader = ReaderFactory.Async(source);
        var actual = await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => reader.ReadAsync());
        Assert.AreEqual(cancellation.Token, actual.CancellationToken);
        Assert.IsFalse(await reader.ReadAsync());
        Assert.AreEqual(1, source.Disposals);
    }

    [TestMethod]
    public async Task AsyncDisposalFailuresPropagateFromExplicitEofAndNextResultOnlyOnce()
    {
        foreach (var mode in new[] { "explicit", "eof", "next", "close" })
        {
            var error = new ReaderFailure(mode);
            var source = new TrackingAsyncSource<ReaderRow>(new ReaderRow()) { DisposeError = error };
            var reader = ReaderFactory.Async(source);
            Assert.IsTrue(await reader.ReadAsync());
            Exception actual;
            if (mode == "close") actual = Assert.ThrowsExactly<ReaderFailure>(() => reader.Close());
            else actual = await Assert.ThrowsExactlyAsync<ReaderFailure>(async () =>
            {
                if (mode == "explicit") await reader.DisposeAsync();
                else if (mode == "eof") await reader.ReadAsync();
                else await reader.NextResultAsync();
            });
            Assert.AreSame(error, actual);
            await reader.DisposeAsync();
            reader.Close();
            Assert.AreEqual(1, source.Disposals);
        }
    }

    [TestMethod]
    public async Task HasRowsWaitsForGenuinelyAsynchronousMoveAndCachesItsResult()
    {
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var source = new TrackingAsyncSource<ReaderRow>(new ReaderRow()) { MoveGate = gate, MoveStarted = started };
        await using var reader = ReaderFactory.Async(source, "Id");
        var prefetch = Task.Run(() => reader.HasRows);
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.IsFalse(prefetch.IsCompleted);
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => reader.ReadAsync());
        }
        finally { gate.TrySetResult(true); }
        Assert.IsTrue(await prefetch.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.AreEqual(1, source.Moves);
        Assert.IsTrue(await reader.ReadAsync());
        Assert.AreEqual(42, reader.GetInt32(0));
        Assert.AreEqual(1, source.Moves);
    }

    [TestMethod]
    public async Task AsyncHasRowsNullAndMoveFailuresTerminateAndRelease()
    {
        foreach (var nullRow in new[] { false, true })
        {
            var failure = new ReaderFailure("prefetch");
            var source = new TrackingAsyncSource<ReaderRow>(new ReaderRow());
            if (nullRow) source = new TrackingAsyncSource<ReaderRow>(new ReaderRow[] { null! });
            else source.MoveError = failure;
            await using var reader = ReaderFactory.Async(source, "Id");
            if (nullRow) Assert.ThrowsExactly<InvalidOperationException>(() => _ = reader.HasRows);
            else Assert.AreSame(failure, Assert.ThrowsExactly<ReaderFailure>(() => _ = reader.HasRows));
            Assert.IsFalse(await reader.ReadAsync());
            Assert.IsFalse(reader.HasRows);
            Assert.AreEqual(1, source.Moves);
            Assert.AreEqual(1, source.Disposals);
        }
    }

    [TestMethod]
    public async Task AsyncFailureAfterOneRowInvalidatesCurrentAndKeepsHasRows()
    {
        var failure = new ReaderFailure("second move");
        var source = new TrackingAsyncSource<ReaderRow>(new(), new()) { MoveError = failure, FailAtMove = 2 };
        await using var reader = ReaderFactory.Async(source, "Id");
        Assert.IsTrue(await reader.ReadAsync());
        Assert.AreSame(failure, await Assert.ThrowsExactlyAsync<ReaderFailure>(() => reader.ReadAsync()));
        Assert.IsTrue(reader.HasRows);
        Assert.ThrowsExactly<InvalidOperationException>(() => reader.GetValue(0));
        Assert.IsFalse(await reader.ReadAsync());
        Assert.AreEqual(1, source.Disposals);
    }

    [TestMethod]
    public async Task AsyncSourceSupportsSynchronousReadAndCloseAndClosedChecks()
    {
        var source = new TrackingAsyncSource<ReaderRow>(new ReaderRow());
        var reader = ReaderFactory.Async(source, "Id");
        Assert.IsTrue(reader.Read());
        Assert.AreEqual(42, reader.GetInt32(0));
        reader.Close();
        Assert.IsTrue(reader.IsClosed);
        Assert.ThrowsExactly<InvalidOperationException>(() => reader.Read());
        Assert.ThrowsExactly<InvalidOperationException>(() => _ = reader.HasRows);
        Assert.ThrowsExactly<InvalidOperationException>(() => reader.GetSchemaTable());
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => reader.ReadAsync());
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => reader.NextResultAsync());
        await reader.DisposeAsync();
        Assert.AreEqual(1, source.Disposals);
    }
}
