using System.Collections;
using System.Dynamic;
using HKW.FastMember;
using Microsoft.CSharp.RuntimeBinder;
using Common = HKW.FastMember.Abstractions;

namespace HKW.FastMember.Tests;

[TestClass]
public sealed class DynamicReaderContractTests
{
    [TestMethod]
    public void GenericDynamicAccessor_PreservesMetadataAbsenceAndNameDispatchAcrossBridges()
    {
        var model = DynamicModelFactory.Create("Value", 12);
        var typed = TypeAccessor.Create<ExpandoObject>();
        Common.TypeAccessor bridge = typed;
        Assert.IsFalse(typed.GetMembersSupported);
        Assert.AreEqual(typeof(ExpandoObject), typed.TargetType);
        Assert.ThrowsExactly<NotSupportedException>(() => typed.GetMembers());
        Assert.ThrowsExactly<NotSupportedException>(() => typed.GetMemberDictionary());
        Assert.IsTrue(typed.TryGetValue(model, "Value", out var value));
        Assert.AreEqual(12, value);
        typed[model, "Value"] = 23;
        Assert.AreEqual(23, bridge[model, "Value"]);
        Assert.IsTrue(bridge.TrySetValue(model, "Value", 34));
        Assert.AreEqual(34, typed[model, "Value"]);
        Assert.IsFalse(typed.TryGetValue(model, "Missing", out _));
        Assert.ThrowsExactly<ArgumentNullException>(() => typed.TryGetValue(model, null!, out _));
        Assert.ThrowsExactly<ArgumentNullException>(() => bridge.TrySetValue(null!, "Value", 1));
        var wrapper = ObjectAccessor.Create<ExpandoObject>(model);
        Assert.IsTrue(wrapper.TryGetValue("Value", out value));
        Assert.AreEqual(34, value);
    }

    [TestMethod]
    public void DynamicUserExceptions_AreNotReportedAsMissingMembers()
    {
        var accessor = TypeAccessor.Create<ThrowingDynamicModel>();
        var model = new ThrowingDynamicModel();
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            accessor.TryGetValue(model, "Value", out _)
        );
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            accessor.TrySetValue(model, "Value", 1)
        );
    }

    [TestMethod]
    public void DynamicColumns_DeduplicateDescriptorsButPreserveSelectedOrderAndDuplicates()
    {
        var row = DynamicModelFactory.Create("Value", 42);
        ((IDictionary<string, object?>)row)["Other"] = null;
        using var reader = ObjectReader.Create(new[] { row }, "Value", "Other", "Value");
        Assert.AreEqual(3, reader.FieldCount);
        Assert.AreEqual(0, reader.GetOrdinal("Value"));
        Assert.IsTrue(
            reader.GetColumnSchema().All(c => c.DataType == typeof(object) && c.AllowDBNull == true)
        );
        Assert.IsTrue(reader.Read());
        Assert.AreEqual(42, reader.GetValue(0));
        Assert.AreSame(DBNull.Value, reader.GetValue(1));
        Assert.AreEqual(42, reader.GetValue(2));
        Assert.IsFalse(reader.Read());
    }

    [TestMethod]
    public void DynamicEmptySource_HasStableSchemaWithoutEnumeration()
    {
        var source = new ProbeEnumerable<ExpandoObject>([]);
        using var reader = ObjectReader.Create(source, "Value", "Value");
        Assert.AreEqual(0, source.Acquisitions);
        var schema = reader.GetSchemaTable();
        Assert.IsNotNull(schema);
        Assert.AreEqual(2, schema.Rows.Count);
        Assert.AreEqual(typeof(object), reader.GetFieldType(0));
        Assert.AreEqual(0, source.Acquisitions);
        Assert.IsFalse(reader.Read());
        Assert.AreEqual(1, source.Acquisitions);
        Assert.AreEqual(1, source.Disposals);
        reader.Close();
        Assert.AreEqual(1, source.Disposals);
    }

    [TestMethod]
    public void DynamicMissingColumn_ThrowsBinderErrorRatherThanFabricatingNull()
    {
        var source = new ProbeEnumerable<ExpandoObject>([DynamicModelFactory.Create()]);
        using var reader = ObjectReader.Create(source, "Missing");
        Assert.IsTrue(reader.Read());
        Assert.ThrowsExactly<RuntimeBinderException>(() => reader.GetValue(0));
        reader.Close();
        Assert.AreEqual(1, source.Disposals);
    }

    [TestMethod]
    public void DynamicSchemaValidation_DoesNotEnumerateOrDiscoverRuntimeObjectRows()
    {
        var source = new ProbeEnumerable<ExpandoObject>([DynamicModelFactory.Create()]);
        Assert.ThrowsExactly<InvalidOperationException>(() => ObjectReader.Create(source));
        Assert.ThrowsExactly<ArgumentNullException>(() =>
            ObjectReader.Create(source, (string[])null!)
        );
        Assert.ThrowsExactly<ArgumentNullException>(() =>
            ObjectReader.Create(source, new string[] { null! })
        );
        Assert.AreEqual(0, source.Acquisitions);
        var objects = new ProbeEnumerable<object>([DynamicModelFactory.Create("Value")]);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            ObjectReader.Create(objects, "Value")
        );
        Assert.AreEqual(0, objects.Acquisitions);
        using var reader = ObjectReader.Create(objects);
        Assert.AreEqual(0, reader.FieldCount);
    }

    [TestMethod]
    public async Task DynamicAsyncColumns_PreserveSchemaDuplicatesTokenAndExactlyOnceDisposal()
    {
        var source = new TrackingAsyncEnumerable<ExpandoObject>(
            [DynamicModelFactory.Create("Value", 42)],
            completeAsynchronously: true
        );
        using var cancellation = new CancellationTokenSource();
        var reader = ObjectReader.Create(source, cancellation.Token, "Value", "Value");
        Assert.AreEqual(2, reader.GetColumnSchema().Count);
        Assert.AreEqual(0, source.MoveNextCount);
        Assert.IsTrue(await reader.ReadAsync());
        Assert.AreEqual(cancellation.Token, source.EnumeratorCancellationToken);
        Assert.AreEqual(42, reader.GetValue(0));
        Assert.AreEqual(42, reader.GetValue(1));
        Assert.IsFalse(await reader.ReadAsync());
        await reader.DisposeAsync();
        reader.Close();
        Assert.AreEqual(1, source.DisposeCount);
    }

    [TestMethod]
    public async Task DynamicAsyncEmptySource_HasSchemaAndDisposesAfterEnd()
    {
        var source = new TrackingAsyncEnumerable<ExpandoObject>([]);
        await using var reader = ObjectReader.Create(source, "Name");
        Assert.AreEqual(typeof(object), reader.GetFieldType(0));
        Assert.AreEqual(true, reader.GetColumnSchema()[0].AllowDBNull);
        Assert.IsFalse(await reader.ReadAsync());
        Assert.AreEqual(1, source.DisposeCount);
    }

    [TestMethod]
    public async Task UnstartedReaders_DoNotAcquireOrDisposeSources()
    {
        var sync = new ProbeEnumerable<MemberModel>([new MemberModel()]);
        var syncReader = ObjectReader.Create(sync);
        syncReader.Close();
        syncReader.Dispose();
        Assert.AreEqual(0, sync.Acquisitions);
        Assert.AreEqual(0, sync.Disposals);
        var asyncSource = new TrackingAsyncEnumerable<ExpandoObject>([new ExpandoObject()]);
        var asyncReader = ObjectReader.Create(asyncSource, "Value");
        await asyncReader.DisposeAsync();
        Assert.AreEqual(0, asyncSource.MoveNextCount);
        Assert.AreEqual(0, asyncSource.DisposeCount);
    }

    [TestMethod]
    public async Task StructRows_WorkThroughSharedSyncAndAsyncReaders()
    {
        var rows = new[]
        {
            new StructModel { Property = 42, Field = "boxed" },
        };
        using var sync = ObjectReader.Create(rows, "Property", "Field");
        Assert.IsTrue(sync.Read());
        Assert.AreEqual(42, sync.GetInt32(0));
        Assert.AreEqual("boxed", sync.GetString(1));
        var source = new TrackingAsyncEnumerable<StructModel>(rows);
        await using var asyncReader = ObjectReader.Create(source, "Field", "Property");
        Assert.IsTrue(await asyncReader.ReadAsync());
        Assert.AreEqual("boxed", asyncReader.GetString(0));
        Assert.AreEqual(42, asyncReader.GetInt32(1));
    }

    [TestMethod]
    public void DefaultColumns_ExcludeWriteOnlyAndUnsupportedMembers()
    {
        using var reader = ObjectReader.Create(new[] { new MemberModel() });
        Assert.IsFalse(
            reader.GetColumnSchema().Any(c => c.ColumnName == nameof(MemberModel.WriteOnlyProperty))
        );
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            ObjectReader.Create(new[] { new MemberModel() }, nameof(MemberModel.WriteOnlyProperty))
        );
        Assert.IsTrue(reader.Read());
        Assert.AreEqual("read-only", reader[nameof(MemberModel.ReadOnlyProperty)]);
    }

    public sealed class ThrowingDynamicModel : DynamicObject
    {
        public override bool TryGetMember(GetMemberBinder binder, out object? result) =>
            throw new InvalidOperationException("dynamic getter");

        public override bool TrySetMember(SetMemberBinder binder, object? value) =>
            throw new InvalidOperationException("dynamic setter");
    }

    private sealed class ProbeEnumerable<T>(IEnumerable<T> source) : IEnumerable<T>
    {
        public int Acquisitions { get; private set; }
        public int Disposals { get; private set; }

        public IEnumerator<T> GetEnumerator()
        {
            Acquisitions++;
            return new Enumerator(this, source.GetEnumerator());
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        private sealed class Enumerator(ProbeEnumerable<T> owner, IEnumerator<T> inner)
            : IEnumerator<T>
        {
            public T Current => inner.Current;
            object IEnumerator.Current => Current!;

            public bool MoveNext() => inner.MoveNext();

            public void Reset() => throw new NotSupportedException();

            public void Dispose()
            {
                owner.Disposals++;
                inner.Dispose();
            }
        }
    }
}
