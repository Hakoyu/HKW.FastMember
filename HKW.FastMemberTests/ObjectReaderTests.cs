using System.Collections;
using System.Data;
using System.Data.Common;
using HKW.FastMember;

namespace HKW.FastMemberTests;

[TestClass]
public sealed class ObjectReaderTests
{
    [TestMethod]
    public void Create_WithoutColumns_UsesOrdinalMetadataOrder()
    {
        var row = new OrdinalModel
        {
            First = 1,
            Middle = "two",
            Last = "three",
        };
        using var reader = ObjectReader.Create([row]);

        Assert.AreEqual(3, reader.FieldCount);
        CollectionAssert.AreEqual(
            new[] { "First", "Middle", "Last" },
            Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToArray()
        );
        Assert.IsTrue(reader.Read());
        CollectionAssert.AreEqual(new object[] { 1, "two", "three" }, ReadValues(reader));
    }

    [TestMethod]
    public void DataTableLoad_ConsumesRowsAndHonorsPartialOrdinalOrdering()
    {
        var rows = new[]
        {
            new PartiallyOrdinalModel
            {
                First = "one",
                Middle = 1,
                Last = 10,
            },
            new PartiallyOrdinalModel
            {
                First = "two",
                Middle = null,
                Last = 20,
            },
        };
        var table = new DataTable();

        using (var reader = ObjectReader.Create(rows))
            table.Load(reader);

        CollectionAssert.AreEqual(
            new[]
            {
                nameof(PartiallyOrdinalModel.First),
                nameof(PartiallyOrdinalModel.Middle),
                nameof(PartiallyOrdinalModel.Last),
            },
            table.Columns.Cast<DataColumn>().Select(column => column.ColumnName).ToArray()
        );
        Assert.AreEqual("one", table.Rows[0][0]);
        Assert.AreEqual(1, table.Rows[0][1]);
        Assert.AreEqual(10, table.Rows[0][2]);
        Assert.AreEqual("two", table.Rows[1][0]);
        Assert.AreSame(DBNull.Value, table.Rows[1][1]);
        Assert.AreEqual(20, table.Rows[1][2]);
    }

    [TestMethod]
    public void Create_WithColumns_PreservesOrderAndUsesExactCaseNames()
    {
        using var reader = ObjectReader.Create(
            [
                new OrdinalModel
                {
                    First = 7,
                    Middle = "middle",
                    Last = "last",
                },
            ],
            nameof(OrdinalModel.Last),
            nameof(OrdinalModel.First)
        );

        Assert.AreEqual(0, reader.GetOrdinal(nameof(OrdinalModel.Last)));
        Assert.AreEqual(1, reader.GetOrdinal(nameof(OrdinalModel.First)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => reader.GetOrdinal("last"));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => reader.GetOrdinal("Missing"));

        Assert.IsTrue(reader.Read());
        Assert.AreEqual("last", reader[nameof(OrdinalModel.Last)]);
        Assert.AreEqual(7, reader[1]);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => _ = reader["last"]);
    }

    [TestMethod]
    public void Schema_ReportsNamesTypesOrdinalsSizesAndNullability()
    {
        using var reader = ObjectReader.Create(
            [new SchemaModel()],
            nameof(SchemaModel.RequiredNumber),
            nameof(SchemaModel.OptionalNumber),
            nameof(SchemaModel.Text)
        );

        var schema = reader.GetSchemaTable();
        Assert.AreEqual(3, schema.Rows.Count);
        AssertSchemaRow(schema.Rows[0], 0, "RequiredNumber", typeof(int), false);
        AssertSchemaRow(schema.Rows[1], 1, "OptionalNumber", typeof(int), true);
        AssertSchemaRow(schema.Rows[2], 2, "Text", typeof(string), true);

        var columns = ((IDbColumnSchemaGenerator)reader).GetColumnSchema();
        Assert.AreEqual(3, columns.Count);
        Assert.AreEqual("OptionalNumber", columns[1].ColumnName);
        Assert.AreEqual(1, columns[1].ColumnOrdinal);
        Assert.AreEqual(typeof(int), columns[1].DataType);
        Assert.AreEqual(-1, columns[1].ColumnSize);
        Assert.AreEqual(true, columns[1].AllowDBNull);
        Assert.AreEqual(false, columns[1].IsKey);
        Assert.AreSame(columns, ((IDbColumnSchemaGenerator)reader).GetColumnSchema());
    }

    [TestMethod]
    public void NullValues_AreExposedAsDBNull()
    {
        using var reader = ObjectReader.Create([
            new NullableModel { NullableNumber = null, NullableText = null },
        ]);

        Assert.IsTrue(reader.Read());
        Assert.AreSame(DBNull.Value, reader.GetValue(0));
        Assert.AreSame(DBNull.Value, reader[nameof(NullableModel.NullableText)]);
        Assert.IsTrue(reader.IsDBNull(0));
        Assert.IsTrue(reader.IsDBNull(1));
    }

    [TestMethod]
    public void TypedGetters_ReturnUnderlyingValues()
    {
        var guid = Guid.NewGuid();
        var timestamp = new DateTime(2026, 10, 21, 12, 0, 0, DateTimeKind.Utc);
        var row = new TypedModel
        {
            Boolean = true,
            Byte = 2,
            Character = 'x',
            DateTime = timestamp,
            Decimal = 3.5m,
            Double = 4.5d,
            Float = 5.5f,
            Guid = guid,
            Int16 = 6,
            Int32 = 7,
            Int64 = 8,
            String = "nine",
        };
        string[] columns =
        [
            nameof(TypedModel.Boolean),
            nameof(TypedModel.Byte),
            nameof(TypedModel.Character),
            nameof(TypedModel.DateTime),
            nameof(TypedModel.Decimal),
            nameof(TypedModel.Double),
            nameof(TypedModel.Float),
            nameof(TypedModel.Guid),
            nameof(TypedModel.Int16),
            nameof(TypedModel.Int32),
            nameof(TypedModel.Int64),
            nameof(TypedModel.String),
        ];
        using var reader = ObjectReader.Create([row], columns);

        Assert.IsTrue(reader.Read());
        Assert.IsTrue(reader.GetBoolean(0));
        Assert.AreEqual((byte)2, reader.GetByte(1));
        Assert.AreEqual('x', reader.GetChar(2));
        Assert.AreEqual(timestamp, reader.GetDateTime(3));
        Assert.AreEqual(3.5m, reader.GetDecimal(4));
        Assert.AreEqual(4.5d, reader.GetDouble(5));
        Assert.AreEqual(5.5f, reader.GetFloat(6));
        Assert.AreEqual(guid, reader.GetGuid(7));
        Assert.AreEqual((short)6, reader.GetInt16(8));
        Assert.AreEqual(7, reader.GetInt32(9));
        Assert.AreEqual(8L, reader.GetInt64(10));
        Assert.AreEqual("nine", reader.GetString(11));
        Assert.AreEqual(typeof(Guid), reader.GetFieldType(7));
        Assert.AreEqual(nameof(Guid), reader.GetDataTypeName(7));
    }

    [TestMethod]
    public void GetValues_CopiesOnlyAvailableDestinationSlots()
    {
        using var reader = ObjectReader.Create([
            new NullableModel { NullableNumber = 42, NullableText = null },
        ]);
        Assert.IsTrue(reader.Read());

        var shortBuffer = new object[1];
        Assert.AreEqual(1, reader.GetValues(shortBuffer));
        Assert.AreEqual(42, shortBuffer[0]);

        var longBuffer = new object[] { "old", "old", "sentinel" };
        Assert.AreEqual(2, reader.GetValues(longBuffer));
        CollectionAssert.AreEqual(new object[] { 42, DBNull.Value, "sentinel" }, longBuffer);

        Assert.AreEqual(0, reader.GetValues([]));
        Assert.ThrowsExactly<NullReferenceException>(() => reader.GetValues(null!));
    }

    [TestMethod]
    public void GetBytes_SupportsLengthQueriesPartialCopiesAndEndOfValue()
    {
        using var reader = ObjectReader.Create(
            [new BufferModel { Bytes = [10, 20, 30, 40], Text = "abcd" }],
            nameof(BufferModel.Bytes),
            nameof(BufferModel.Text)
        );
        Assert.IsTrue(reader.Read());

        Assert.AreEqual(4L, reader.GetBytes(0, -1, null, -1, -1));
        var destination = new byte[] { 0, 0, 0, 0, 0 };
        Assert.AreEqual(2L, reader.GetBytes(0, 1, destination, 2, 2));
        CollectionAssert.AreEqual(new byte[] { 0, 0, 20, 30, 0 }, destination);
        Assert.AreEqual(1L, reader.GetBytes(0, 3, destination, 0, 4));
        Assert.AreEqual((byte)40, destination[0]);
        Assert.AreEqual(0L, reader.GetBytes(0, 4, destination, 0, 1));
        Assert.AreEqual(0L, reader.GetBytes(0, 100, destination, 0, 1));
    }

    [TestMethod]
    public void GetChars_SupportsLengthQueriesPartialCopiesAndEndOfValue()
    {
        using var reader = ObjectReader.Create(
            [new BufferModel { Bytes = [1], Text = "abcd" }],
            nameof(BufferModel.Bytes),
            nameof(BufferModel.Text)
        );
        Assert.IsTrue(reader.Read());

        Assert.AreEqual(4L, reader.GetChars(1, -1, null, -1, -1));
        var destination = new[] { '_', '_', '_', '_', '_' };
        Assert.AreEqual(2L, reader.GetChars(1, 1, destination, 2, 2));
        CollectionAssert.AreEqual(new[] { '_', '_', 'b', 'c', '_' }, destination);
        Assert.AreEqual(1L, reader.GetChars(1, 3, destination, 0, 4));
        Assert.AreEqual('d', destination[0]);
        Assert.AreEqual(0L, reader.GetChars(1, 4, destination, 0, 1));
        Assert.AreEqual(0L, reader.GetChars(1, 100, destination, 0, 1));
    }

    [TestMethod]
    [DataRow(-1L, 0, 1, typeof(ArgumentOutOfRangeException))]
    [DataRow(2147483648L, 0, 1, typeof(ArgumentOutOfRangeException))]
    [DataRow(0L, -1, 1, typeof(ArgumentOutOfRangeException))]
    [DataRow(0L, 0, -1, typeof(ArgumentOutOfRangeException))]
    [DataRow(0L, 2, 2, typeof(ArgumentException))]
    public void GetBytes_ValidatesOffsetsAndDestinationBounds(
        long fieldOffset,
        int bufferOffset,
        int length,
        Type exceptionType
    )
    {
        using var reader = ObjectReader.Create([new BufferModel { Bytes = [1], Text = "a" }]);
        Assert.IsTrue(reader.Read());

        var exception = Assert.Throws<Exception>(() =>
            reader.GetBytes(0, fieldOffset, new byte[3], bufferOffset, length)
        );
        Assert.AreEqual(exceptionType, exception.GetType());
    }

    [TestMethod]
    [DataRow(-1L, 0, 1, typeof(ArgumentOutOfRangeException))]
    [DataRow(2147483648L, 0, 1, typeof(ArgumentOutOfRangeException))]
    [DataRow(0L, -1, 1, typeof(ArgumentOutOfRangeException))]
    [DataRow(0L, 0, -1, typeof(ArgumentOutOfRangeException))]
    [DataRow(0L, 2, 2, typeof(ArgumentException))]
    public void GetChars_ValidatesOffsetsAndDestinationBounds(
        long fieldOffset,
        int bufferOffset,
        int length,
        Type exceptionType
    )
    {
        using var reader = ObjectReader.Create([new BufferModel { Bytes = [1], Text = "a" }]);
        Assert.IsTrue(reader.Read());

        var exception = Assert.Throws<Exception>(() =>
            reader.GetChars(1, fieldOffset, new char[3], bufferOffset, length)
        );
        Assert.AreEqual(exceptionType, exception.GetType());
    }

    [TestMethod]
    public void ReaderState_RequiresCurrentRowAndClearsItAfterEnd()
    {
        using var reader = ObjectReader.Create([
            new NullableModel { NullableNumber = 1, NullableText = "one" },
        ]);

        Assert.Throws<Exception>(() => reader.GetValue(0));
        Assert.IsTrue(reader.Read());
        Assert.AreEqual(1, reader.GetInt32(0));
        Assert.IsFalse(reader.Read());
        Assert.IsFalse(reader.HasRows);
        Assert.Throws<Exception>(() => reader.GetValue(0));
        Assert.IsFalse(reader.Read());
    }

    [TestMethod]
    public void ReaderMetadataAndResultState_MatchSingleResultReaderContract()
    {
        using var reader = ObjectReader.Create([new NullableModel()]);

        Assert.AreEqual(0, reader.Depth);
        Assert.AreEqual(0, reader.RecordsAffected);
        Assert.IsTrue(reader.HasRows);
        Assert.IsFalse(reader.IsClosed);
        Assert.IsFalse(reader.NextResult());
        Assert.IsFalse(reader.HasRows);
        Assert.IsFalse(reader.Read());
        Assert.IsFalse(reader.IsClosed);
    }

    [TestMethod]
    public void CloseAndDispose_AreIdempotentAndDisposeSourceOnce()
    {
        var source = new TrackingEnumerable<NullableModel>([new NullableModel()]);
        var reader = ObjectReader.Create(source);

        reader.Close();
        reader.Close();
        reader.Dispose();

        Assert.IsTrue(reader.IsClosed);
        Assert.IsFalse(reader.HasRows);
        Assert.IsFalse(reader.Read());
        Assert.AreEqual(1, source.DisposeCount);
        Assert.Throws<Exception>(() => reader.GetValue(0));
    }

    [TestMethod]
    public void UnknownExplicitField_HasObjectSchemaAndFailsWhenRead()
    {
        using var reader = ObjectReader.Create([new NullableModel()], "Missing");

        Assert.AreEqual(typeof(object), reader.GetFieldType(0));
        Assert.AreEqual(nameof(Object), reader.GetDataTypeName(0));
        Assert.IsTrue(reader.Read());
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => reader.GetValue(0));
    }

    [TestMethod]
    public void EmptyInput_ReadsNoRowsAndCanBeClosed()
    {
        using var reader = ObjectReader.Create(
            Array.Empty<NullableModel>(),
            nameof(NullableModel.NullableNumber)
        );

        Assert.IsTrue(reader.HasRows);
        Assert.IsFalse(reader.Read());
        Assert.IsFalse(reader.HasRows);
        Assert.IsFalse(reader.NextResult());
        reader.Close();
        Assert.IsTrue(reader.IsClosed);
    }

    [TestMethod]
    public void PublicFactoryAndSchemaApis_HandleNullMembersEmptySchemaAndNullSource()
    {
        PartiallyOrdinalModel[] rows =
        [
            new()
            {
                First = "one",
                Middle = 1,
                Last = 2,
            },
        ];
        using var reader = ObjectReader.Create(rows, (string[])null!);

        Assert.AreEqual(3, reader.FieldCount);
        Assert.IsTrue(reader.Read());
        Assert.AreEqual("one", reader[nameof(PartiallyOrdinalModel.First)]);

        using var emptySchemaReader = ObjectReader.Create(new[] { new EmptyModel() });
        Assert.HasCount(0, emptySchemaReader.GetSchemaTable().Rows);
        Assert.HasCount(0, ((IDbColumnSchemaGenerator)emptySchemaReader).GetColumnSchema());

        var nullSourceError = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            ObjectReader.Create((IEnumerable<MemberModel>)null!, nameof(MemberModel.Name))
        );
        Assert.AreEqual("source", nullSourceError.ParamName);
    }

    [TestMethod]
    public void EnumeratorAndNestedReaderApis_FollowDbDataReaderContract()
    {
        using var reader = ObjectReader.Create(
            [new MemberModel { Name = "first", Number = 1 }],
            nameof(MemberModel.Name),
            nameof(MemberModel.Number)
        );

        var enumerator = reader.GetEnumerator();
        Assert.IsTrue(enumerator.MoveNext());
        Assert.AreEqual("first", ((IDataRecord)enumerator.Current).GetString(0));
        Assert.IsFalse(enumerator.MoveNext());
        Assert.ThrowsExactly<NotSupportedException>(() => reader.GetData(0));
    }

    [TestMethod]
    public void DynamicRows_RequireExplicitMembersAndAreReadableWhenProvided()
    {
        var row = DynamicModelFactory.Create("Value", 42);
        Assert.ThrowsExactly<InvalidOperationException>(() => ObjectReader.Create(new[] { row }));

        using var reader = ObjectReader.Create(new[] { row }, "Value");

        Assert.AreEqual(typeof(object), reader.GetFieldType(0));
        Assert.IsTrue(reader.Read());
        Assert.AreEqual(42, reader.GetInt32(0));
        Assert.AreEqual(42, reader["Value"]);
        Assert.Throws<Exception>(() => _ = reader["value"]);
    }

    private static object[] ReadValues(DbDataReader reader)
    {
        var values = new object[reader.FieldCount];
        reader.GetValues(values);
        return values;
    }

    private static void AssertSchemaRow(
        DataRow row,
        int ordinal,
        string name,
        Type type,
        bool allowNull
    )
    {
        Assert.AreEqual(ordinal, row["ColumnOrdinal"]);
        Assert.AreEqual(name, row["ColumnName"]);
        Assert.AreEqual(type, row["DataType"]);
        Assert.AreEqual(-1, row["ColumnSize"]);
        Assert.AreEqual(allowNull, row["AllowDBNull"]);
        Assert.AreEqual(false, row["IsKey"]);
    }

    private sealed class EmptyModel { }

    private sealed class SchemaModel
    {
        public int RequiredNumber { get; set; }
        public int? OptionalNumber { get; set; }
        public string? Text { get; set; }
    }

    private sealed class TypedModel
    {
        public bool Boolean { get; set; }
        public byte Byte { get; set; }
        public char Character { get; set; }
        public DateTime DateTime { get; set; }
        public decimal Decimal { get; set; }
        public double Double { get; set; }
        public float Float { get; set; }
        public Guid Guid { get; set; }
        public short Int16 { get; set; }
        public int Int32 { get; set; }
        public long Int64 { get; set; }
        public string String { get; set; } = string.Empty;
    }

    private sealed class BufferModel
    {
        public byte[] Bytes { get; set; } = [];
        public string Text { get; set; } = string.Empty;
    }

    private sealed class TrackingEnumerable<T>(IEnumerable<T> items) : IEnumerable<T>
    {
        public int DisposeCount { get; private set; }

        public IEnumerator<T> GetEnumerator() =>
            new TrackingEnumerator(this, items.GetEnumerator());

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        private sealed class TrackingEnumerator(TrackingEnumerable<T> owner, IEnumerator<T> inner)
            : IEnumerator<T>
        {
            public T Current => inner.Current;
            object IEnumerator.Current => Current!;

            public bool MoveNext() => inner.MoveNext();

            public void Reset() => inner.Reset();

            public void Dispose()
            {
                owner.DisposeCount++;
                inner.Dispose();
            }
        }
    }
}
