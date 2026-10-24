using System.Data.Common;
using HKW.FastMember.Abstractions;

namespace HKW.FastMember.Abstractions.Tests;

[TestClass]
public sealed class ObjectReaderTests
{
    [TestMethod]
    public void CreateAndSchemaAreLazyAndSchemaCopiesCannotPolluteMetadata()
    {
        var source = new TrackingSource<ReaderRow>();
        using var reader = ReaderFactory.Sync(source, "Nullable", "Id", "Text", "Id");
        Assert.AreEqual(4, reader.FieldCount);
        Assert.AreEqual(1, reader.GetOrdinal("Id"));
        Assert.AreEqual(typeof(int), reader.GetFieldType(0));
        Assert.AreEqual("Int32", reader.GetDataTypeName(0));
        var columns = reader.GetColumnSchema();
        using var table = reader.GetSchemaTable()!;
        for (var i = 0; i < columns.Count; i++)
        {
            Assert.AreEqual(reader.GetName(i), columns[i].ColumnName);
            Assert.AreEqual(i, columns[i].ColumnOrdinal);
            Assert.AreEqual(reader.GetFieldType(i), columns[i].DataType);
            Assert.AreEqual(false, columns[i].IsKey);
            Assert.AreEqual(columns[i].ColumnName, table.Rows[i]["ColumnName"]);
            Assert.AreEqual(i, table.Rows[i]["ColumnOrdinal"]);
            Assert.AreEqual(columns[i].DataType, table.Rows[i]["DataType"]);
            Assert.AreEqual(columns[i].AllowDBNull, table.Rows[i]["AllowDBNull"]);
            Assert.AreEqual(false, table.Rows[i]["IsKey"]);
        }
        Assert.AreEqual(true, columns[0].AllowDBNull);
        Assert.AreEqual(false, columns[1].AllowDBNull);
        Assert.AreEqual(true, columns[2].AllowDBNull);
        table.Rows[0]["ColumnName"] = "corrupted";
        using var fresh = reader.GetSchemaTable()!;
        Assert.AreEqual("Nullable", fresh.Rows[0]["ColumnName"]);
        Assert.AreNotSame(columns, reader.GetColumnSchema());
        Assert.AreEqual(0, source.Acquired);
        Assert.AreEqual(0, source.Moves);
        Assert.IsFalse(reader.HasRows);
        Assert.AreEqual(1, source.Acquired);
        Assert.AreEqual(1, source.Moves);
        Assert.AreEqual(1, source.Disposals);
        Assert.IsFalse(reader.Read());
        Assert.IsFalse(reader.Read());
        Assert.AreEqual(1, source.Moves);
    }

    [TestMethod]
    public void DefaultsSortByOrdinalThenOrdinalNameAndExcludeUnreadable()
    {
        using var reader = ReaderFactory.Sync(Array.Empty<ReaderRow>());
        var names = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToArray();
        CollectionAssert.AreEqual(
            new[]
            {
                "Boolean",
                "Byte",
                "Bytes",
                "Character",
                "Chars",
                "Date",
                "Decimal",
                "Double",
                "Float",
                "Guid",
                "Long",
                "Nullable",
                "Short",
                "Throwing",
                "Id",
                "Text",
            },
            names
        );
    }

    [TestMethod]
    public void ExplicitColumnsAreSnapshottedAndDuplicatesRemain()
    {
        string[] names = ["Text", "Id", "Text"];
        using var reader = ReaderFactory.Sync(new[] { new ReaderRow() }, names);
        names[0] = "Bad";
        Assert.IsTrue(reader.Read());
        Assert.AreEqual("hello", reader[0]);
        Assert.AreEqual("hello", reader[2]);
        Assert.AreEqual(0, reader.GetOrdinal("Text"));
        Assert.AreEqual(42, reader["Id"]);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => reader.GetOrdinal("id"));
        Assert.ThrowsExactly<ArgumentNullException>(() => reader.GetOrdinal(null!));
    }

    [TestMethod]
    public void AllCreateValidationPrecedesEnumeratorAcquisition()
    {
        var source = new TrackingSource<ReaderRow>();
        Assert.ThrowsExactly<ArgumentNullException>(() => ReaderFactory.Sync<ReaderRow>(null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => ReaderFactory.Sync(source, null!));
        Assert.ThrowsExactly<ArgumentNullException>(() =>
            ReaderFactory.Sync(source, new string[] { null! })
        );
        Assert.AreEqual(
            "members",
            Assert
                .ThrowsExactly<ArgumentOutOfRangeException>(() =>
                    ReaderFactory.Sync(source, "missing")
                )
                .ParamName
        );
        Assert.AreEqual(
            "members",
            Assert
                .ThrowsExactly<ArgumentOutOfRangeException>(() =>
                    ReaderFactory.Sync(source, "WriteOnly")
                )
                .ParamName
        );
        Assert.AreEqual(0, source.Acquired);
    }

    [TestMethod]
    public void HasRowsPrefetchesExactlyOnceWithoutExposingOrSkippingFirstRow()
    {
        var source = new TrackingSource<ReaderRow>(new() { Id = 1 }, new() { Id = 2 });
        using var reader = ReaderFactory.Sync(source, "Id");
        Assert.ThrowsExactly<InvalidOperationException>(() => reader.GetInt32(0));
        Assert.IsTrue(reader.HasRows);
        Assert.IsTrue(reader.HasRows);
        Assert.AreEqual(1, source.Moves);
        Assert.AreEqual(1, source.Currents);
        Assert.ThrowsExactly<InvalidOperationException>(() => reader.GetValue(0));
        Assert.IsTrue(reader.Read());
        Assert.AreEqual(1, reader.GetInt32(0));
        Assert.AreEqual(1, source.Moves);
        Assert.IsTrue(reader.Read());
        Assert.AreEqual(2, reader.GetInt32(0));
        Assert.IsFalse(reader.Read());
        Assert.IsTrue(reader.HasRows);
        Assert.ThrowsExactly<InvalidOperationException>(() => reader.GetValue(0));
        Assert.IsFalse(reader.Read());
        Assert.AreEqual(3, source.Moves);
        Assert.AreEqual(1, source.Disposals);
    }

    [TestMethod]
    public void FirstReadEstablishesHasRowsAndNextResultEndsAndDisposes()
    {
        var source = new TrackingSource<ReaderRow>(new(), new());
        using var reader = ReaderFactory.Sync(source, "Id");
        Assert.IsTrue(reader.Read());
        Assert.IsTrue(reader.HasRows);
        Assert.AreEqual(1, source.Moves);
        Assert.IsFalse(reader.NextResult());
        Assert.IsFalse(reader.HasRows);
        Assert.IsFalse(reader.Read());
        Assert.IsFalse(reader.NextResult());
        Assert.ThrowsExactly<InvalidOperationException>(() => reader.GetValue(0));
        Assert.AreEqual(1, source.Moves);
        Assert.AreEqual(1, source.Disposals);
    }

    [TestMethod]
    public async Task NextResultAndDisposalBeforeReadingNeverAcquireEnumerator()
    {
        var source = new TrackingSource<ReaderRow>(new ReaderRow());
        var reader = ReaderFactory.Sync(source);
        Assert.IsFalse(await reader.NextResultAsync());
        Assert.IsFalse(reader.Read());
        Assert.IsFalse(reader.HasRows);
        reader.Close();
        reader.Dispose();
        await reader.DisposeAsync();
        Assert.AreEqual(0, source.Acquired);
        Assert.AreEqual(0, source.Disposals);
        var source2 = new TrackingSource<ReaderRow>(new ReaderRow());
        await ReaderFactory.Sync(source2).DisposeAsync();
        Assert.AreEqual(0, source2.Acquired);
    }

    [TestMethod]
    public void StrongGettersAndDBNullUseCastsNotConversions()
    {
        var row = new ReaderRow();
        using var reader = ReaderFactory.Sync(new[] { row });
        Assert.IsTrue(reader.Read());
        Assert.AreEqual(row.Boolean, reader.GetBoolean(reader.GetOrdinal("Boolean")));
        Assert.AreEqual(row.Byte, reader.GetByte(reader.GetOrdinal("Byte")));
        Assert.AreEqual(row.Character, reader.GetChar(reader.GetOrdinal("Character")));
        Assert.AreEqual(row.Date, reader.GetDateTime(reader.GetOrdinal("Date")));
        Assert.AreEqual(row.Decimal, reader.GetDecimal(reader.GetOrdinal("Decimal")));
        Assert.AreEqual(row.Double, reader.GetDouble(reader.GetOrdinal("Double")));
        Assert.AreEqual(row.Float, reader.GetFloat(reader.GetOrdinal("Float")));
        Assert.AreEqual(row.Guid, reader.GetGuid(reader.GetOrdinal("Guid")));
        Assert.AreEqual(row.Id, reader.GetInt32(reader.GetOrdinal("Id")));
        Assert.AreEqual(row.Long, reader.GetInt64(reader.GetOrdinal("Long")));
        Assert.AreEqual(row.Short, reader.GetInt16(reader.GetOrdinal("Short")));
        Assert.AreEqual(row.Text, reader.GetString(reader.GetOrdinal("Text")));
        Assert.ThrowsExactly<InvalidCastException>(() => reader.GetInt64(reader.GetOrdinal("Id")));
        var nullable = reader.GetOrdinal("Nullable");
        Assert.AreSame(DBNull.Value, reader.GetValue(nullable));
        Assert.IsTrue(reader.IsDBNull(nullable));
        Assert.ThrowsExactly<InvalidCastException>(() => reader.GetFieldValue<int?>(nullable));
        row.Nullable = 9;
        Assert.AreEqual(9, reader.GetFieldValue<int?>(nullable));
        Assert.IsFalse(reader.IsDBNull(nullable));
        row.Text = null;
        Assert.AreSame(DBNull.Value, reader["Text"]);
        Assert.ThrowsExactly<InvalidCastException>(() =>
            reader.GetString(reader.GetOrdinal("Text"))
        );
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => reader.GetValue(-1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => reader.GetValue(reader.FieldCount));
    }

    [TestMethod]
    public void GetValuesUsesDestinationLengthAndValidatesState()
    {
        using var reader = ReaderFactory.Sync(new[] { new ReaderRow() }, "Id", "Nullable", "Id");
        Assert.ThrowsExactly<InvalidOperationException>(() => reader.GetValues([]));
        Assert.IsTrue(reader.Read());
        Assert.ThrowsExactly<ArgumentNullException>(() => reader.GetValues(null!));
        Assert.AreEqual(0, reader.GetValues([]));
        object[] shortBuffer = new object[1];
        Assert.AreEqual(1, reader.GetValues(shortBuffer));
        Assert.AreEqual(42, shortBuffer[0]);
        object[] longBuffer = [0, 0, 0, "untouched"];
        Assert.AreEqual(3, reader.GetValues(longBuffer));
        CollectionAssert.AreEqual(new object[] { 42, DBNull.Value, 42, "untouched" }, longBuffer);
    }

    [TestMethod]
    public void BytesAndCharsRespectLengthsOffsetsAndTypes()
    {
        using var reader = ReaderFactory.Sync(
            new[] { new ReaderRow() },
            "Bytes",
            "Text",
            "Chars",
            "Id"
        );
        Assert.IsTrue(reader.Read());
        Assert.AreEqual(4L, reader.GetBytes(0, 0, null, 0, 0));
        Assert.AreEqual(5L, reader.GetChars(1, 0, null, 0, 0));
        Assert.AreEqual(3L, reader.GetChars(2, 0, null, 0, 0));
        byte[] bytes = new byte[5];
        Assert.AreEqual(3L, reader.GetBytes(0, 1, bytes, 1, 4));
        CollectionAssert.AreEqual(new byte[] { 0, 2, 3, 4, 0 }, bytes);
        char[] chars = new char[5];
        Assert.AreEqual(3L, reader.GetChars(1, 2, chars, 1, 4));
        CollectionAssert.AreEqual(new char[] { '\0', 'l', 'l', 'o', '\0' }, chars);
        Assert.AreEqual(2L, reader.GetChars(2, 1, chars, 0, 2));
        Assert.AreEqual('b', chars[0]);
        Assert.AreEqual('c', chars[1]);
        Assert.AreEqual(0L, reader.GetBytes(0, long.MaxValue, bytes, 5, 0));
        Assert.AreEqual(0L, reader.GetChars(1, long.MaxValue, chars, 5, 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            reader.GetBytes(0, -1, bytes, 0, 1)
        );
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            reader.GetBytes(0, 0, bytes, -1, 1)
        );
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            reader.GetBytes(0, 0, bytes, 0, -1)
        );
        Assert.ThrowsExactly<ArgumentException>(() =>
            reader.GetBytes(0, 0, bytes, 1, int.MaxValue)
        );
        Assert.ThrowsExactly<ArgumentException>(() => reader.GetChars(1, 0, chars, 6, 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            reader.GetChars(1, -1, chars, 0, 1)
        );
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            reader.GetChars(1, 0, chars, -1, 1)
        );
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            reader.GetChars(1, 0, chars, 0, -1)
        );
        Assert.ThrowsExactly<InvalidCastException>(() => reader.GetBytes(3, 0, null, 0, 0));
        Assert.ThrowsExactly<InvalidCastException>(() => reader.GetChars(3, 0, null, 0, 0));
    }

    [TestMethod]
    public async Task ClosedReaderRejectsDataMetadataAndMovementButDisposalIsIdempotent()
    {
        var source = new TrackingSource<ReaderRow>(new ReaderRow());
        var reader = ReaderFactory.Sync(source, "Id");
        Assert.IsTrue(reader.Read());
        reader.Close();
        Assert.IsTrue(reader.IsClosed);
        Assert.AreEqual(0, reader.Depth);
        Assert.AreEqual(0, reader.RecordsAffected);
        Action[] operations =
        [
            () => reader.Read(),
            () => reader.NextResult(),
            () => _ = reader.HasRows,
            () => _ = reader.FieldCount,
            () => reader.GetValue(0),
            () => reader.GetValues([]),
            () => reader.GetName(0),
            () => reader.GetOrdinal("Id"),
            () => reader.GetFieldType(0),
            () => reader.GetSchemaTable(),
            () => reader.GetColumnSchema(),
            () => reader.GetEnumerator(),
        ];
        foreach (var operation in operations)
            Assert.ThrowsExactly<InvalidOperationException>(operation);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => reader.ReadAsync());
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => reader.NextResultAsync());
        reader.Close();
        reader.Dispose();
        await reader.DisposeAsync();
        Assert.AreEqual(1, source.Disposals);
    }

    [TestMethod]
    public void NullRowsTerminateAndInvalidateBothReadAndPrefetch()
    {
        foreach (var prefetch in new[] { false, true })
        {
            var source = new TrackingSource<ReaderRow>(null!, new());
            using var reader = ReaderFactory.Sync(source, "Id");
            Assert.ThrowsExactly<InvalidOperationException>(() =>
            {
                if (prefetch)
                    _ = reader.HasRows;
                else
                    reader.Read();
            });
            Assert.ThrowsExactly<InvalidOperationException>(() => reader.GetValue(0));
            Assert.IsFalse(reader.Read());
            Assert.IsFalse(reader.HasRows);
            Assert.AreEqual(1, source.Moves);
            Assert.AreEqual(1, source.Disposals);
        }
    }

    [TestMethod]
    public void EnumerationAndCurrentFailuresPreserveOriginalAndReleaseOnce()
    {
        foreach (var currentFailure in new[] { false, true })
        {
            var original = new ReaderFailure("enumeration");
            var source = new TrackingSource<ReaderRow>(new ReaderRow())
            {
                DisposeError = new ReaderFailure("cleanup"),
            };
            if (currentFailure)
                source.CurrentError = original;
            else
                source.MoveError = original;
            var reader = ReaderFactory.Sync(source);
            Assert.AreSame(original, Assert.ThrowsExactly<ReaderFailure>(() => reader.Read()));
            Assert.IsFalse(reader.Read());
            Assert.ThrowsExactly<InvalidOperationException>(() => reader.GetValue(0));
            reader.Dispose();
            Assert.AreEqual(1, source.Disposals);
            Assert.AreEqual(1, source.Moves);
        }
    }

    [TestMethod]
    public void FailureAfterSuccessfulRowClearsCurrentButRemembersHasRows()
    {
        var original = new ReaderFailure("later move");
        var source = new TrackingSource<ReaderRow>(new(), new())
        {
            MoveError = original,
            FailAtMove = 2,
        };
        using var reader = ReaderFactory.Sync(source, "Id");
        Assert.IsTrue(reader.Read());
        Assert.AreSame(original, Assert.ThrowsExactly<ReaderFailure>(() => reader.Read()));
        Assert.IsTrue(reader.HasRows);
        Assert.ThrowsExactly<InvalidOperationException>(() => reader.GetValue(0));
        Assert.IsFalse(reader.Read());
        Assert.AreEqual(1, source.Disposals);
    }

    [TestMethod]
    public void AcquisitionFailuresAreTerminalAndDoNotDisposeMissingEnumerator()
    {
        var failure = new ReaderFailure("acquire");
        var source = new TrackingSource<ReaderRow>() { AcquireError = failure };
        using var reader = ReaderFactory.Sync(source);
        Assert.AreSame(failure, Assert.ThrowsExactly<ReaderFailure>(() => _ = reader.HasRows));
        Assert.IsFalse(reader.Read());
        Assert.AreEqual(1, source.Acquired);
        Assert.AreEqual(0, source.Disposals);
    }

    [TestMethod]
    public async Task ExplicitAndTerminalDisposalFailuresPropagateOnlyOnce()
    {
        foreach (var mode in new[] { "close", "async", "eof", "next" })
        {
            var failure = new ReaderFailure(mode);
            var source = new TrackingSource<ReaderRow>(new ReaderRow()) { DisposeError = failure };
            var reader = ReaderFactory.Sync(source);
            Assert.IsTrue(reader.Read());
            Exception actual;
            if (mode == "async")
                actual = await Assert.ThrowsExactlyAsync<ReaderFailure>(() =>
                    reader.DisposeAsync().AsTask()
                );
            else
                actual = Assert.ThrowsExactly<ReaderFailure>(() =>
                {
                    if (mode == "close")
                        reader.Close();
                    else if (mode == "eof")
                        reader.Read();
                    else
                        reader.NextResult();
                });
            Assert.AreSame(failure, actual);
            reader.Close();
            await reader.DisposeAsync();
            Assert.AreEqual(1, source.Disposals);
        }
    }

    [TestMethod]
    public async Task SyncReadAsyncCancellationPreservesCacheAndCurrentRow()
    {
        using var cancellation = new CancellationTokenSource();
        var source = new TrackingSource<ReaderRow>(new() { Id = 1 }, new() { Id = 2 });
        using var reader = ReaderFactory.Sync(source, "Id");
        Assert.IsTrue(reader.HasRows);
        cancellation.Cancel();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            reader.ReadAsync(cancellation.Token)
        );
        Assert.AreEqual(1, source.Moves);
        Assert.IsTrue(await reader.ReadAsync());
        Assert.AreEqual(1, reader.GetInt32(0));
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            reader.ReadAsync(cancellation.Token)
        );
        Assert.AreEqual(1, reader.GetInt32(0));
        Assert.IsTrue(await reader.ReadAsync());
        Assert.AreEqual(2, reader.GetInt32(0));
    }

    [TestMethod]
    public void ReentrantReadIsRejectedWithoutDrivingEnumeratorTwice()
    {
        var source = new TrackingSource<ReaderRow>(new ReaderRow());
        using var reader = ReaderFactory.Sync(source);
        source.OnMove = () => Assert.ThrowsExactly<InvalidOperationException>(() => reader.Read());
        Assert.IsTrue(reader.Read());
        Assert.AreEqual(1, source.Moves);
    }

    [TestMethod]
    public void GetterExceptionsPropagateWithoutTerminatingEnumeration()
    {
        var failure = new ReaderFailure("getter");
        using var reader = ReaderFactory.Sync(
            new[]
            {
                new ReaderRow { GetterException = failure },
                new ReaderRow(),
            },
            "Throwing"
        );
        Assert.IsTrue(reader.Read());
        Assert.AreSame(failure, Assert.ThrowsExactly<ReaderFailure>(() => reader.GetValue(0)));
        Assert.IsTrue(reader.Read());
        Assert.AreEqual(42, reader.GetInt32(0));
    }
}
