using Microsoft.CodeAnalysis;

namespace HKW.FastMember.Generater.Tests;

[TestClass]
public sealed class SharedContractMigrationTests
{
    [TestMethod]
    public void RuntimeExposesOnlySharedContractsAndUniqueOrdinalMarker()
    {
        var runtime = typeof(TypeAccessor).Assembly;
        Assert.AreEqual(
            "HKW.FastMember.Abstractions",
            typeof(Abstractions.OrdinalAttribute).Assembly.GetName().Name
        );
        foreach (
            var name in new[] { "Member", "OrdinalAttribute", "TypeAccessor`1", "ObjectAccessor`1" }
        )
            Assert.IsNull(runtime.GetType("HKW.FastMember.Generater." + name));
        var accessor = TypeAccessor.Create<MigrationModel>();
        Assert.IsInstanceOfType<Abstractions.TypeAccessor<MigrationModel>>(accessor);
        Assert.IsInstanceOfType<Abstractions.TypeAccessor>(accessor);
        Assert.AreEqual(typeof(MigrationModel), accessor.TargetType);
        Assert.AreEqual(4, accessor.GetMemberDictionary()["Value"].Ordinal);
        Assert.IsInstanceOfType<Abstractions.ObjectAccessor<MigrationModel>>(
            ObjectAccessor.Create(new MigrationModel())
        );
        using var reader = ObjectReader.Create(new MigrationModel[0]);
        Assert.IsInstanceOfType<Abstractions.ObjectReader>(reader);
    }

    [TestMethod]
    public async Task ReflectionNamespaceFactoriesNeverTriggerGeneratedDiscoveryOrDiagnostics()
    {
        var reflection = GeneratorTestHelper.Compilation(
            """
            namespace HKW.FastMember {
                public static class TypeAccessor { public static object Create<T>() => new object(); }
                public static class ObjectAccessor { public static object Create<T>(T source) => source!; }
                public static class ObjectReader { public static object Create<T>(T[] source) => source; }
            }
            """,
            "ReflectionFactoryFixture"
        );
        var reference = MetadataReference.CreateFromImage(
            GeneratorTestHelper.Emit(reflection),
            new MetadataReferenceProperties(aliases: ["reflection"])
        );
        var result = await GeneratorTestHelper.Run(
            """
            extern alias reflection;
            public static class Entry {
                public static object Test() {
                    System.Func<object> factory = reflection::HKW.FastMember.TypeAccessor.Create<object>;
                    var accessor = reflection::HKW.FastMember.TypeAccessor.Create<object>();
                    var bound = reflection::HKW.FastMember.ObjectAccessor.Create(new object());
                    var reader = reflection::HKW.FastMember.ObjectReader.Create(new object[0]);
                    dynamic source = new object();
                    return reflection::HKW.FastMember.ObjectAccessor.Create(source);
                }
            }
            """,
            extra: [reference]
        );
        GeneratorTestHelper.AssertCompiles(result);
        Assert.AreEqual(0, result.Diagnostics.Length);
        Assert.AreEqual(0, result.Driver.GetRunResult().GeneratedTrees.Length);
    }

    [TestMethod]
    public async Task CommonReaderFactoriesNeitherDiscoverNorRegisterGeneratedAccessors()
    {
        var result = await GeneratorTestHelper.Run(
            """
            using HKW.FastMember.Abstractions;
            public class Model { }
            public class ManualAccessor : TypeAccessor<Model> {
                public ManualAccessor() : base(new Member[0], false) { }
                protected override object? GetValueTypedCore(Model source, int index) => throw new System.NotSupportedException();
                protected override void SetValueTypedCore(Model source, int index, object? value) => throw new System.NotSupportedException();
            }
            public static class Entry {
                public static object Test() {
                    var accessor = new ManualAccessor();
                    using var sync = ObjectReader.Create(new Model[0], accessor);
                    using var asyncReader = ObjectReader.Create(Items(), accessor);
                    using var tokenReader = ObjectReader.Create(Items(), accessor, default(System.Threading.CancellationToken));
                    try { HKW.FastMember.Generater.GeneratedAccessorRegistry<Model>.GetAccessor(); return false; }
                    catch (System.InvalidOperationException) { return true; }
                }
                private static async System.Collections.Generic.IAsyncEnumerable<Model> Items() {
                    await System.Threading.Tasks.Task.CompletedTask;
                    yield break;
                }
            }
            """
        );
        Assert.AreEqual(true, GeneratorTestHelper.Execute(result));
        Assert.AreEqual(0, result.Diagnostics.Length);
        Assert.AreEqual(0, result.Driver.GetRunResult().GeneratedTrees.Length);
    }

    [TestMethod]
    [DataRow("HKW.FastMember.Abstractions")]
    [DataRow("HKW.FastMember.Generater")]
    public async Task OrdinalRecognitionRequiresTheSharedMarkerAssembly(string markerNamespace)
    {
        var foreign = GeneratorTestHelper.Compilation(
            """
            namespace MARKER_NAMESPACE {
                public sealed class OrdinalAttribute(int value) : System.Attribute { }
            }
            """.Replace("MARKER_NAMESPACE", markerNamespace),
            "ForeignMarkers"
        );
        var reference = MetadataReference.CreateFromImage(
            GeneratorTestHelper.Emit(foreign),
            new MetadataReferenceProperties(aliases: ["foreign"])
        );
        var result = await GeneratorTestHelper.Run(
            """
            extern alias foreign;
            using HKW.FastMember.Generater;
            public class Model {
                [foreign::MARKER_NAMESPACE.Ordinal(9)] public int Foreign { get; set; }
                [HKW.FastMember.Abstractions.Ordinal(3)] public int Shared { get; set; }
            }
            public static class Entry {
                public static object Test() {
                    var members = TypeAccessor.Create<Model>().GetMemberDictionary();
                    return members["Foreign"].Ordinal == -1 && members["Shared"].Ordinal == 3;
                }
            }
            """.Replace("MARKER_NAMESPACE", markerNamespace),
            extra: [reference]
        );
        Assert.AreEqual(true, GeneratorTestHelper.Execute(result));
    }

    public sealed class MigrationModel
    {
        [Abstractions.Ordinal(4)]
        public int Value { get; set; }
    }
}
