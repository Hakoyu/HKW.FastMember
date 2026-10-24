using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace HKW.FastMember.Abstractions.Tests;

[TestClass]
public sealed class ReaderContractTests
{
    [TestMethod]
    public void InjectedAccessorSupportsStructRowsAndDataTableLoad()
    {
        using var reader = ObjectReader.Create(new[] { new ValueRow(1, null), new ValueRow(2, 3) }, new ValueAccessor());
        using var table = new DataTable();
        table.Load(reader);
        Assert.AreEqual(2, table.Rows.Count);
        Assert.AreEqual(typeof(int), table.Columns["Optional"]!.DataType);
        Assert.IsTrue(table.Columns["Optional"]!.AllowDBNull);
        Assert.AreEqual(DBNull.Value, table.Rows[0]["Optional"]);
        Assert.AreEqual(3, table.Rows[1]["Optional"]);
    }

    [TestMethod]
    public async Task InjectedAccessorSupportsAsyncStructRows()
    {
        var source = new TrackingAsyncSource<ValueRow>(new ValueRow(7, 9));
        await using var reader = ObjectReader.Create(source, new ValueAccessor());
        Assert.IsTrue(await reader.ReadAsync());
        Assert.AreEqual(7, reader.GetInt32(0));
        Assert.AreEqual(9, reader.GetInt32(1));
        Assert.IsFalse(await reader.ReadAsync());
        Assert.AreEqual(1, source.Disposals);
    }

    [TestMethod]
    public void AccessorCompatibilityIsCheckedBeforeAcquiringSource()
    {
        var source = new TrackingSource<ReaderRow>();
        Assert.ThrowsExactly<ArgumentException>(() => ObjectReader.Create(source, new ValueAccessor()));
        Assert.ThrowsExactly<ArgumentNullException>(() => ObjectReader.Create(source, null!));
        Assert.AreEqual(0, source.Acquired);
        var widened = new TrackingSource<object>(new ValueRow(1, 2));
        Assert.ThrowsExactly<ArgumentException>(() => ObjectReader.Create(widened, new ValueAccessor()));
        Assert.AreEqual(0, widened.Acquired);
    }

    [TestMethod]
    public void StaticBaseAccessorAcceptsDerivedSourceWithoutDiscoveringDerivedMembers()
    {
        using var reader = ObjectReader.Create(new[] { new DerivedRow { Value = 11, Extra = 12 } }, new BaseAccessor());
        Assert.AreEqual(1, reader.FieldCount);
        Assert.IsTrue(reader.Read());
        Assert.AreEqual(11, reader.GetInt32(0));
    }

    [TestMethod]
    public void ReaderRetainsDamContractAndHasNoBackendDependencies()
    {
        using var reader = ObjectReader.Create(Array.Empty<ValueRow>(), new ValueAccessor());
        var required = DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.PublicProperties;
        Assert.AreEqual(required, reader.GetType().GetMethod(nameof(DbDataReader.GetFieldType))!.ReturnParameter.GetCustomAttribute<DynamicallyAccessedMembersAttribute>()!.MemberTypes);
        Assert.AreEqual(required, typeof(Member).GetProperty(nameof(Member.EffectiveType))!.GetCustomAttribute<DynamicallyAccessedMembersAttribute>()!.MemberTypes);
        Assert.IsNull(typeof(ObjectReader).GetCustomAttribute<RequiresUnreferencedCodeAttribute>());
        var dependencies = typeof(ObjectReader).Assembly.GetReferencedAssemblies().Select(a => a.Name!).ToArray();
        Assert.IsFalse(dependencies.Any(n => n.StartsWith("HKW.FastMember") || n.StartsWith("Microsoft.CodeAnalysis") || n == "HKW.SourceGeneratorUtils"));
    }

    private readonly record struct ValueRow(int Id, int? Optional);
    private sealed class ValueAccessor() : TypeAccessor(typeof(ValueRow), [
        new("Id", typeof(int), -1, true, false, "value.Id", typeof(int), false),
        new("Optional", typeof(int?), -1, true, false, "value.Optional", typeof(int), true)
    ], false)
    {
        protected override object? GetValueCore(object target, int index) => index == 0 ? ((ValueRow)target).Id : ((ValueRow)target).Optional;
        protected override void SetValueCore(object target, int index, object? value) => throw new NotSupportedException();
    }
    private class BaseRow { public int Value { get; set; } }
    private sealed class DerivedRow : BaseRow { public int Extra { get; set; } }
    private sealed class BaseAccessor() : TypeAccessor<BaseRow>([new("Value", typeof(int), -1, true, false, "base.Value", typeof(int), false)], false)
    {
        protected override object? GetValueTypedCore(BaseRow target, int index) => target.Value;
        protected override void SetValueTypedCore(BaseRow target, int index, object? value) => throw new NotSupportedException();
    }
}
