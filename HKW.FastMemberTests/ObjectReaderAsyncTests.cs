using HKW.FastMember;

namespace HKW.FastMemberTests;

[TestClass]
public class ObjectReaderAsyncTests
{
    [TestMethod]
    public async Task ReadAsync_ReadsSequentialValues()
    {
        var source = new TrackingAsyncEnumerable<MemberModel>(
            [
                new MemberModel { Name = "first", Number = 1 },
                new MemberModel { Name = "second", Number = 2 },
            ],
            completeAsynchronously: true
        );
        var reader = ObjectReader.Create(source, "Name", "Number");

        Assert.IsTrue(await reader.ReadAsync());
        Assert.AreEqual("first", reader.GetString(0));
        Assert.AreEqual(1, reader.GetInt32(1));
        Assert.IsTrue(await reader.ReadAsync());
        Assert.AreEqual("second", reader.GetString(0));
        Assert.AreEqual(2, reader.GetInt32(1));
        Assert.IsFalse(await reader.ReadAsync());
        Assert.IsFalse(await reader.ReadAsync());
        Assert.AreEqual(3, source.MoveNextCount);
        Assert.IsFalse(reader.HasRows);
        Assert.IsFalse(reader.IsClosed);

        await reader.DisposeAsync();
        Assert.AreEqual(1, source.DisposeCount);
    }

    [TestMethod]
    public async Task Create_WithExplicitColumns_PreservesColumnOrder()
    {
        var source = new TrackingAsyncEnumerable<MemberModel>([
            new MemberModel { Name = "value", Number = 42 },
        ]);
        var reader = ObjectReader.Create(source, "Number", "Name");

        Assert.AreEqual(2, reader.FieldCount);
        Assert.AreEqual("Number", reader.GetName(0));
        Assert.AreEqual("Name", reader.GetName(1));
        Assert.AreEqual(0, reader.GetOrdinal("Number"));
        Assert.AreEqual(1, reader.GetOrdinal("Name"));
        Assert.IsTrue(await reader.ReadAsync());
        Assert.AreEqual(42, reader.GetInt32(0));
        Assert.AreEqual("value", reader.GetString(1));

        await reader.DisposeAsync();
    }

    [TestMethod]
    public async Task ReadAsync_WithEmptyEnumeration_ReturnsFalseWithoutClosingReader()
    {
        var source = new TrackingAsyncEnumerable<MemberModel>([]);
        var reader = ObjectReader.Create(source, "Name");

        Assert.IsFalse(await reader.ReadAsync());
        Assert.IsFalse(await reader.ReadAsync());
        Assert.AreEqual(1, source.MoveNextCount);
        Assert.IsFalse(reader.HasRows);
        Assert.IsFalse(reader.IsClosed);

        await reader.DisposeAsync();
        Assert.IsTrue(reader.IsClosed);
        Assert.AreEqual(1, source.DisposeCount);
    }

    [TestMethod]
    public async Task Create_OverloadsValidateNullAndForwardCancellationToken()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() =>
            ObjectReader.Create<MemberModel>((IAsyncEnumerable<MemberModel>)null!, "Name")
        );
        Assert.ThrowsExactly<ArgumentNullException>(() =>
            ObjectReader.Create(
                (IAsyncEnumerable<MemberModel>)null!,
                CancellationToken.None,
                "Name"
            )
        );

        using var cancellation = new CancellationTokenSource();
        var source = new TrackingAsyncEnumerable<MemberModel>([]);
        await using var reader = ObjectReader.Create(source, cancellation.Token, "Name");

        Assert.AreEqual(cancellation.Token, source.EnumeratorCancellationToken);
    }

    [TestMethod]
    public void Create_WithPreCanceledToken_ThrowsBeforeCreatingEnumerator()
    {
        var source = new TrackingAsyncEnumerable<MemberModel>([]);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.ThrowsExactly<OperationCanceledException>(() =>
            ObjectReader.Create(source, cancellation.Token, "Name")
        );
        Assert.AreEqual(default, source.EnumeratorCancellationToken);
        Assert.AreEqual(0, source.MoveNextCount);
        Assert.AreEqual(0, source.DisposeCount);
    }

    [TestMethod]
    public async Task ReadAsync_WithPreCanceledToken_ReturnsCanceledTaskWithoutAdvancing()
    {
        var source = new TrackingAsyncEnumerable<MemberModel>([new MemberModel { Name = "value" }]);
        var reader = ObjectReader.Create(source, "Name");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var pending = reader.ReadAsync(cancellation.Token);

        Assert.IsTrue(pending.IsCanceled);
        await Assert.ThrowsExactlyAsync<TaskCanceledException>(async () => await pending);
        Assert.AreEqual(0, source.MoveNextCount);
        Assert.IsTrue(await reader.ReadAsync());
        Assert.AreEqual("value", reader.GetString(0));

        await reader.DisposeAsync();
    }

    [TestMethod]
    public async Task ReadAsync_WhenMoveNextAsyncThrows_PropagatesOriginalException()
    {
        var expected = new InvalidOperationException("move next failed");
        var source = new TrackingAsyncEnumerable<MemberModel>(
            [],
            expected,
            completeAsynchronously: true
        );
        var reader = ObjectReader.Create(source, "Name");

        var actual = await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
            await reader.ReadAsync()
        );

        Assert.AreSame(expected, actual);
        Assert.AreEqual(1, source.MoveNextCount);
        await reader.DisposeAsync();
        Assert.AreEqual(1, source.DisposeCount);
    }

    [TestMethod]
    public async Task Close_DisposesEnumeratorOnceAndClosesReader()
    {
        var source = new TrackingAsyncEnumerable<MemberModel>([new MemberModel { Name = "value" }]);
        var reader = ObjectReader.Create(source, "Name");

        reader.Close();
        reader.Close();
        reader.Dispose();
        await reader.DisposeAsync();

        Assert.IsTrue(reader.IsClosed);
        Assert.IsFalse(reader.HasRows);
        Assert.IsFalse(await reader.ReadAsync());
        Assert.AreEqual(1, source.DisposeCount);
    }

    [TestMethod]
    public async Task Dispose_DisposesEnumeratorOnceAndClosesReader()
    {
        var source = new TrackingAsyncEnumerable<MemberModel>([new MemberModel { Name = "value" }]);
        var reader = ObjectReader.Create(source, "Name");

        reader.Dispose();
        reader.Dispose();
        await reader.DisposeAsync();

        Assert.IsTrue(reader.IsClosed);
        Assert.IsFalse(reader.HasRows);
        Assert.IsFalse(await reader.ReadAsync());
        Assert.AreEqual(1, source.DisposeCount);
    }

    [TestMethod]
    public async Task DisposeAsync_DisposesEnumeratorOnceAndClosesReader()
    {
        var source = new TrackingAsyncEnumerable<MemberModel>([new MemberModel { Name = "value" }]);
        var reader = ObjectReader.Create(source, "Name");

        await reader.DisposeAsync();
        await reader.DisposeAsync();
        reader.Dispose();
        reader.Close();

        Assert.IsTrue(reader.IsClosed);
        Assert.IsFalse(reader.HasRows);
        Assert.IsFalse(await reader.ReadAsync());
        Assert.AreEqual(1, source.DisposeCount);
    }

    [TestMethod]
    public void Read_SynchronouslyAdvancesAsyncEnumerator()
    {
        var source = new TrackingAsyncEnumerable<MemberModel>(
            [new MemberModel { Name = "value", Number = 42 }],
            completeAsynchronously: true
        );
        using var reader = ObjectReader.Create(source, "Name", "Number");

        Assert.IsTrue(reader.Read());
        Assert.AreEqual("value", reader.GetString(0));
        Assert.AreEqual(42, reader.GetInt32(1));
        Assert.IsFalse(reader.Read());
    }
}
