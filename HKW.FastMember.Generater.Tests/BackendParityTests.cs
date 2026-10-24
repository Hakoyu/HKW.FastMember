using System.Data;
using HKW.FastMember.Abstractions;
using GeneratedTypes = HKW.FastMember.Generater.TypeAccessor;
using ReflectionTypes = HKW.FastMember.TypeAccessor;
using SharedReader = HKW.FastMember.Abstractions.ObjectReader;

namespace HKW.FastMember.Generater.Tests;

[TestClass]
public sealed class BackendParityTests
{
    [TestMethod]
    public void DefaultMetadataAndMemberPermissionsAreIdentical()
    {
        var generated = GeneratedTypes.Create<ParityRow>();
        var reflected = ReflectionTypes.Create<ParityRow>();
        Assert.AreEqual(generated.TargetType, reflected.TargetType);
        CollectionAssert.AreEqual(Metadata(generated), Metadata(reflected));
        CollectionAssert.AreEqual(
            generated.GetMembers().Select(m => m.Name).Order(StringComparer.Ordinal).ToArray(),
            generated.GetMembers().Select(m => m.Name).ToArray()
        );
        foreach (
            var accessor in new Abstractions.TypeAccessor<ParityRow>[] { generated, reflected }
        )
        {
            var row = new ParityRow();
            accessor[row, "Id"] = 12;
            accessor[row, "Name"] = "value";
            accessor[row, "WriteOnly"] = 13;
            Assert.AreEqual(12, accessor[row, "Id"]);
            Assert.AreEqual("value", accessor[row, "Name"]);
            Assert.AreEqual(13, row.Written);
            Assert.AreEqual(4, accessor[row, "Hidden"]);
            Assert.AreEqual(6, accessor[row, "Virtual"]);
            foreach (var name in new[] { "ReadOnly", "Init", "PrivateSet", "ReadOnlyField" })
            {
                Assert.IsFalse(accessor.TrySetValue(row, name, 1), name);
                Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => accessor[row, name] = 1);
            }
            Assert.IsFalse(accessor.TryGetValue(row, "WriteOnly", out var missing));
            Assert.IsNull(missing);
            Assert.IsFalse(accessor.GetMemberDictionary().ContainsKey("Static"));
            Assert.IsFalse(accessor.GetMemberDictionary().ContainsKey("Constant"));
            Assert.IsFalse(accessor.GetMemberDictionary().ContainsKey("Item"));
        }
    }

    [TestMethod]
    public void PublicExceptionsAndUserErrorsMatchWithoutBeingSwallowed()
    {
        var generated = GeneratedTypes.Create<ParityRow>();
        var reflected = ReflectionTypes.Create<ParityRow>();
        foreach (
            var accessor in new Abstractions.TypeAccessor<ParityRow>[] { generated, reflected }
        )
        {
            var row = new ParityRow();
            Assert.ThrowsExactly<ArgumentNullException>(() =>
                accessor.TryGetValue(null!, "Id", out _)
            );
            Assert.ThrowsExactly<ArgumentNullException>(() =>
                accessor.TryGetValue(row, null!, out _)
            );
            Assert.AreEqual(
                "name",
                Assert
                    .ThrowsExactly<ArgumentOutOfRangeException>(() => _ = accessor[row, "missing"])
                    .ParamName
            );
            Assert.IsFalse(accessor.TryGetValue(row, "missing", out var value));
            Assert.IsNull(value);
            Assert.IsFalse(accessor.TrySetValue(row, "missing", 1));
            Assert.ThrowsExactly<InvalidCastException>(() =>
                accessor.TrySetValue(row, "Id", "bad")
            );
            Assert.ThrowsExactly<NullReferenceException>(() =>
                accessor.TrySetValue(row, "Id", null)
            );
            Assert.AreSame(
                row.Error,
                Assert.ThrowsExactly<ParityException>(() =>
                    accessor.TryGetValue(row, "Throwing", out _)
                )
            );
            Assert.AreSame(
                row.Error,
                Assert.ThrowsExactly<ParityException>(() =>
                    accessor.TrySetValue(row, "Throwing", 1)
                )
            );
        }
    }

    [TestMethod]
    public void TypedAndUntypedViewsShareStaticTypeAndVirtualDispatch()
    {
        var generated = GeneratedTypes.Create<ParityBase>();
        var reflected = ReflectionTypes.Create<ParityBase>();
        var row = new ParityRow();
        foreach (
            var accessor in new Abstractions.TypeAccessor<ParityBase>[] { generated, reflected }
        )
        {
            Abstractions.TypeAccessor untyped = accessor;
            Assert.AreEqual(6, accessor[row, "Virtual"]);
            Assert.AreEqual(6, untyped[row, "Virtual"]);
            Assert.IsFalse(accessor.GetMemberDictionary().ContainsKey("Id"));
            accessor[row, "Name"] = "base";
            Assert.AreEqual("base", ((ParityBase)row).Name);
            Assert.AreEqual("derived", row.Name);
        }
        Abstractions.ObjectAccessor<ParityBase> generatedBound =
            global::HKW.FastMember.Generater.ObjectAccessor.Create<ParityBase>(row);
        Abstractions.ObjectAccessor<ParityBase> reflectedBound =
            global::HKW.FastMember.ObjectAccessor.Create<ParityBase>(row);
        Assert.AreSame(row, generatedBound.Source);
        Assert.AreSame(row, reflectedBound.Source);
        Assert.IsFalse(generatedBound.TryGetValue("Id", out _));
        Assert.IsFalse(reflectedBound.TryGetValue("Id", out _));
        var runtimeBound = global::HKW.FastMember.ObjectAccessor.Create((object)row);
        Assert.IsTrue(runtimeBound.TryGetValue("Id", out _));
    }

    [TestMethod]
    public void RequiredInitializationAndAbstractConstructionAgree()
    {
        var requiredGenerated = GeneratedTypes.Create<RequiredRow>();
        var requiredReflected = ReflectionTypes.Create<RequiredRow>();
        Assert.IsFalse(requiredGenerated.CreateNewSupported);
        Assert.IsFalse(requiredReflected.CreateNewSupported);
        Assert.ThrowsExactly<NotSupportedException>(() => requiredGenerated.CreateNew());
        Assert.ThrowsExactly<NotSupportedException>(() => requiredReflected.CreateNew());
        var initializedGenerated = GeneratedTypes.Create<InitializedRow>();
        var initializedReflected = ReflectionTypes.Create<InitializedRow>();
        Assert.IsTrue(initializedGenerated.CreateNewSupported);
        Assert.IsTrue(initializedReflected.CreateNewSupported);
        Assert.AreEqual(8, initializedGenerated.CreateNew().Id);
        Assert.AreEqual(8, initializedReflected.CreateNew().Id);
        Assert.IsFalse(GeneratedTypes.Create<AbstractRow>().CreateNewSupported);
        Assert.IsFalse(ReflectionTypes.Create<AbstractRow>().CreateNewSupported);
    }

    [TestMethod]
    public async Task SharedReaderProducesIdenticalSchemaValuesAndAsyncResults()
    {
        var generated = GeneratedTypes.Create<ParityRow>();
        var reflected = ReflectionTypes.Create<ParityRow>();
        foreach (
            var accessor in new Abstractions.TypeAccessor<ParityRow>[] { generated, reflected }
        )
        {
            using var empty = SharedReader.Create(
                Array.Empty<ParityRow>(),
                accessor,
                "Optional",
                "Id",
                "Id"
            );
            Assert.AreEqual(3, empty.FieldCount);
            Assert.AreEqual(1, empty.GetOrdinal("Id"));
            Assert.AreEqual(typeof(int), empty.GetFieldType(0));
            Assert.AreEqual(true, empty.GetColumnSchema()[0].AllowDBNull);
            using var schema = empty.GetSchemaTable()!;
            Assert.AreEqual(false, schema.Rows[0]["IsKey"]);
            Assert.IsFalse(empty.Read());
            using var sync = SharedReader.Create(
                new[] { new ParityRow { Id = 3 } },
                accessor,
                "Id",
                "Optional"
            );
            using var table = new DataTable();
            table.Load(sync);
            Assert.AreEqual(3, table.Rows[0]["Id"]);
            Assert.AreEqual(DBNull.Value, table.Rows[0]["Optional"]);
            var source = new TrackingAsyncSource<ParityRow>(new ParityRow { Id = 5 });
            await using var asyncReader = SharedReader.Create(source, accessor, "Id", "Optional");
            Assert.IsTrue(await asyncReader.ReadAsync());
            Assert.AreEqual(5, asyncReader.GetInt32(0));
            Assert.IsTrue(asyncReader.IsDBNull(1));
            Assert.IsFalse(await asyncReader.ReadAsync());
            Assert.AreEqual(1, source.Disposals);
        }
    }

    [TestMethod]
    public void BackendCachesRemainSeparateAndReflectionCannotSatisfyMissingGeneration()
    {
        var generated = GeneratedTypes.Create<ParityRow>();
        var reflected = ReflectionTypes.Create<ParityRow>();
        Assert.AreSame(generated, GeneratedTypes.Create<ParityRow>());
        Assert.AreSame(reflected, ReflectionTypes.Create<ParityRow>());
        Assert.AreNotSame(generated, reflected);
        _ = ReflectionTypes.Create<ReflectionOnlyRow>();
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            GeneratedAccessorRegistry<ReflectionOnlyRow>.GetAccessor()
        );
        Assert.IsTrue(
            reflected.GetMembers().All(m => m is global::HKW.FastMember.ReflectionMember)
        );
        Assert.IsTrue(
            generated.GetMembers().All(m => m is not global::HKW.FastMember.ReflectionMember)
        );
        Assert.ThrowsExactly<NotSupportedException>(() =>
            global::HKW.FastMember.ReflectionMemberExtensions.AsReflectionMember(
                generated.GetMembers()[0]
            )
        );
        using var generatedReader = SharedReader.Create(Array.Empty<ParityRow>(), generated, "Id");
        using var reflectedReader = SharedReader.Create(Array.Empty<ParityRow>(), reflected, "Id");
        Assert.AreSame(generatedReader.GetType(), reflectedReader.GetType());
        Assert.AreSame(typeof(Member).Assembly, generatedReader.GetType().Assembly);
        var common = typeof(Member).Assembly.GetReferencedAssemblies();
        Assert.IsFalse(
            common.Any(a =>
                a.Name
                    is "HKW.FastMember"
                        or "HKW.FastMember.Generater"
                        or "HKW.SourceGeneratorUtils"
            )
        );
        Assert.IsFalse(
            typeof(GeneratedTypes)
                .Assembly.GetReferencedAssemblies()
                .Any(a => a.Name == "HKW.FastMember")
        );
        Assert.IsFalse(
            typeof(ReflectionTypes)
                .Assembly.GetReferencedAssemblies()
                .Any(a => a.Name == "HKW.FastMember.Generater")
        );
    }

    private static string[] Metadata(Abstractions.TypeAccessor accessor) =>
        accessor
            .GetMembers()
            .Select(m =>
                $"{m.Name}|{m.Type}|{m.Ordinal}|{m.CanRead}|{m.CanWrite}|{m.EffectiveType}|{m.AllowDBNull}"
            )
            .ToArray();

    public class ParityBase
    {
        public string Name { get; set; } = "base";
        public int Hidden { get; set; } = 4;
        public virtual int Virtual => 5;
    }

    public sealed class ParityRow : ParityBase
    {
        [Abstractions.Ordinal(2)]
        public int Id { get; set; }
        public new string Name = "derived";
        private static new int Hidden => 99;
        public override int Virtual => 6;

        [Abstractions.Ordinal(1)]
        public int? Optional { get; set; }
        public int ReadOnly => 7;
        public readonly int ReadOnlyField = 10;
        public int Init { get; init; }
        public int PrivateSet { get; private set; }
        public int WriteOnly
        {
            set => Written = value;
        }
        public int Written { get; private set; }
        public ParityException Error { get; } = new();
        public int Throwing
        {
            get => throw Error;
            set => throw Error;
        }
        public static int Static { get; set; }
        public const int Constant = 1;
        public int this[int index] => index;
    }

    public sealed class RequiredRow
    {
        public required int Id { get; init; }
    }

    public sealed class InitializedRow
    {
        [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
        public InitializedRow() => Id = 8;

        public required int Id { get; init; }
    }

    public abstract class AbstractRow
    {
        public int Id { get; set; }
    }

    public sealed class ReflectionOnlyRow
    {
        public int Value { get; set; }
    }

    public sealed class ParityException : Exception;
}
