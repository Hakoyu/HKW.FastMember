using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;

namespace HKW.FastMember.Generater.Tests;

[TestClass]
public sealed class MultiAssemblyTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task TwoConsumersRegisterIdenticalContractsInEitherOrder(bool reverse)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var model = GeneratorTestHelper.Compilation(
            """
            namespace Models {
                public class Shared {
                    public int Id { get; set; }
                    public string? Name { get; set; }
                    internal Shared() { }
                    public static Shared New() => new Shared();
                }
            }
            """,
            "Models_" + suffix
        );
        var modelBytes = GeneratorTestHelper.Emit(model);
        var modelReference = MetadataReference.CreateFromImage(modelBytes);
        const string consumer = """
            using HKW.FastMember.Generater;
            using Models;
            public static class Entry {
                public static object Test() {
                    var accessor = TypeAccessor.Create<Shared>();
                    var value = Shared.New();
                    accessor[value, "Id"] = 37;
                    return !accessor.CreateNewSupported && (int)accessor[value, "Id"]! == 37;
                }
                public static object Accessor() => TypeAccessor.Create<Shared>();
            }
            """;
        var first = await GeneratorTestHelper.Run(consumer, "First_" + suffix, [modelReference]);
        var second = await GeneratorTestHelper.Run(consumer, "Second_" + suffix, [modelReference]);
        GeneratorTestHelper.AssertCompiles(first);
        GeneratorTestHelper.AssertCompiles(second);
        Assert.AreEqual(
            first.Driver.GetRunResult().GeneratedTrees.Single().ToString(),
            second.Driver.GetRunResult().GeneratedTrees.Single().ToString()
        );
        var bytes = new[]
        {
            GeneratorTestHelper.Emit(first.Output),
            GeneratorTestHelper.Emit(second.Output),
        };
        if (reverse)
            Array.Reverse(bytes);
        var context = new GeneratorTestHelper.TestLoadContext();
        try
        {
            using var modelStream = new MemoryStream(modelBytes);
            _ = context.LoadFromStream(modelStream);
            object? accessor = null;
            foreach (var image in bytes)
            {
                using var stream = new MemoryStream(image);
                var assembly = context.LoadFromStream(stream);
                RuntimeHelpers.RunModuleConstructor(assembly.ManifestModule.ModuleHandle);
                var entry = assembly.GetType("Entry")!;
                Assert.AreEqual(true, entry.GetMethod("Test")!.Invoke(null, null));
                var next = entry.GetMethod("Accessor")!.Invoke(null, null);
                if (accessor != null)
                    Assert.AreSame(accessor, next);
                accessor = next;
            }
        }
        finally
        {
            context.Unload();
        }
    }

    [TestMethod]
    public async Task ModelAssemblyAndConsumerAgreeOnInternalConstructor()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var producer = await GeneratorTestHelper.Run(
            """
            using HKW.FastMember.Generater;
            namespace Models {
                public class Shared { internal Shared() { } public int Id { get; set; } }
                public static class Factory {
                    public static object Accessor() => TypeAccessor.Create<Shared>();
                }
            }
            """,
            "ModelWithCalls_" + suffix
        );
        GeneratorTestHelper.AssertCompiles(producer);
        var modelBytes = GeneratorTestHelper.Emit(producer.Output);
        var consumer = await GeneratorTestHelper.Run(
            """
            using HKW.FastMember.Generater;
            public static class Entry {
                public static object Test() => !TypeAccessor.Create<Models.Shared>().CreateNewSupported;
                public static object Accessor() => TypeAccessor.Create<Models.Shared>();
            }
            """,
            "App_" + suffix,
            [MetadataReference.CreateFromImage(modelBytes)]
        );
        GeneratorTestHelper.AssertCompiles(consumer);
        var context = new GeneratorTestHelper.TestLoadContext();
        try
        {
            using var modelStream = new MemoryStream(modelBytes);
            var library = context.LoadFromStream(modelStream);
            RuntimeHelpers.RunModuleConstructor(library.ManifestModule.ModuleHandle);
            var expected = library
                .GetType("Models.Factory")!
                .GetMethod("Accessor")!
                .Invoke(null, null);
            using var consumerStream = new MemoryStream(GeneratorTestHelper.Emit(consumer.Output));
            var app = context.LoadFromStream(consumerStream);
            RuntimeHelpers.RunModuleConstructor(app.ManifestModule.ModuleHandle);
            Assert.AreEqual(true, app.GetType("Entry")!.GetMethod("Test")!.Invoke(null, null));
            Assert.AreSame(
                expected,
                app.GetType("Entry")!.GetMethod("Accessor")!.Invoke(null, null)
            );
        }
        finally
        {
            context.Unload();
        }
    }
}
