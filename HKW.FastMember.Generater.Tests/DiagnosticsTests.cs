using Microsoft.CodeAnalysis;

namespace HKW.FastMember.Generater.Tests;

[TestClass]
public sealed class DiagnosticsTests
{
    [TestMethod]
    [DataRow(
        "public static class Entry { public static object Test() => TypeAccessor.Create<object>(); }",
        "HFMG001"
    )]
    [DataRow(
        "public struct Model {} public static class Entry { public static object Test() => TypeAccessor.Create<Model>(); }",
        "HFMG001"
    )]
    [DataRow(
        "public static class Model {} public static class Entry { public static object Test() => TypeAccessor.Create<Model>(); }",
        "HFMG001"
    )]
    [DataRow(
        "public class Model { public int Value; public string Value { get; set; } } public static class Entry { public static object Test() => TypeAccessor.Create<Model>(); }",
        "HFMG006"
    )]
    [DataRow(
        "public interface Model {} public static class Entry { public static object Test() => TypeAccessor.Create<Model>(); }",
        "HFMG001"
    )]
    [DataRow(
        "public class Entry { public object Test<T>() where T : class => TypeAccessor.Create<T>(); }",
        "HFMG001"
    )]
    [DataRow(
        "file class Model {} public static class Entry { public static object Test() => TypeAccessor.Create<Model>(); }",
        "HFMG003"
    )]
    [DataRow(
        "public class Entry { private class Model {} public static object Test() => TypeAccessor.Create<Model>(); }",
        "HFMG003"
    )]
    [DataRow(
        "public static class Entry { public static object Test() => ObjectAccessor.Create(new { Id = 1 }); }",
        "HFMG001"
    )]
    [DataRow(
        "public static class Entry { public static object Test() => TypeAccessor.Create<dynamic>(); }",
        "HFMG001"
    )]
    [DataRow(
        "public static class Entry { public static object Test() => TypeAccessor.Create<System.Dynamic.ExpandoObject>(); }",
        "HFMG001"
    )]
    [DataRow(
        "public class Model { public System.Span<int> Values => default; } public static class Entry { public static object Test() => TypeAccessor.Create<Model>(); }",
        "HFMG002"
    )]
    [DataRow(
        "public class Model { private int number; public ref int Value => ref number; } public static class Entry { public static object Test() => TypeAccessor.Create<Model>(); }",
        "HFMG002"
    )]
    [DataRow(
        "public unsafe class Model { public int* Value => null; } public static class Entry { public static object Test() => TypeAccessor.Create<Model>(); }",
        "HFMG002"
    )]
    [DataRow(
        "public unsafe class Model { public delegate*<void> Value => null; } public static class Entry { public static object Test() => TypeAccessor.Create<Model>(); }",
        "HFMG002"
    )]
    [DataRow(
        "public class Model { [System.Obsolete(\"no\", true)] public int Value { get; set; } } public static class Entry { public static object Test() => TypeAccessor.Create<Model>(); }",
        "HFMG007"
    )]
    public async Task UnsupportedTargetsHaveOneLocatedActionableDiagnostic(string source, string id)
    {
        var result = await GeneratorTestHelper.Run("using HKW.FastMember.Generater;\n" + source);
        Assert.AreEqual(
            1,
            result.Diagnostics.Length,
            string.Join(Environment.NewLine, result.Diagnostics.Select(d => d.ToString()))
        );
        var diagnostic = result.Diagnostics[0];
        Assert.AreEqual(id, diagnostic.Id);
        Assert.AreEqual(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.IsTrue(diagnostic.Location.IsInSource);
        Assert.IsTrue(diagnostic.Location.SourceSpan.Length > 0);
        Assert.IsTrue(diagnostic.GetMessage().Length > 50);
        Assert.AreEqual(0, result.Driver.GetRunResult().GeneratedTrees.Length);
    }

    [TestMethod]
    public async Task MethodGroupsAreRejectedWithoutDuplicateGeneratorDiagnostics()
    {
        var result = await GeneratorTestHelper.Run(
            """
            using HKW.FastMember.Generater;
            public class Model { }
            public static class Entry {
                public static object Test() {
                    System.Func<global::HKW.FastMember.Abstractions.TypeAccessor<Model>> factory = TypeAccessor.Create<Model>;
                    return factory;
                }
            }
            """
        );
        Assert.AreEqual(0, result.GeneratorDiagnostics.Length);
        Assert.AreEqual(1, result.AnalyzerDiagnostics.Length);
        Assert.AreEqual("HFMG004", result.AnalyzerDiagnostics[0].Id);
    }

    [TestMethod]
    public async Task DynamicInvocationIsDiagnosedByAnalyzerOnly()
    {
        var result = await GeneratorTestHelper.Run(
            """
            using HKW.FastMember.Generater;
            public class Model { }
            public static class Entry {
                public static object Test() {
                    dynamic source = new Model();
                    return ObjectAccessor.Create(source);
                }
            }
            """
        );
        Assert.AreEqual(
            1,
            result.Diagnostics.Length,
            string.Join(Environment.NewLine, result.Diagnostics.Select(d => d.ToString()))
        );
        Assert.AreEqual("HFMG005", result.Diagnostics[0].Id);
        Assert.AreEqual(0, result.GeneratorDiagnostics.Length);
    }

    [TestMethod]
    public async Task GeneratedHelpersAndNameofAreNotTreatedAsUnsupportedReferences()
    {
        var result = await GeneratorTestHelper.Run(
            """
            using HKW.FastMember.Generater;
            [System.CodeDom.Compiler.GeneratedCode("Fixture", "1")]
            public static class Helpers {
                public static object Create<T>() where T : class => TypeAccessor.Create<T>();
            }
            public static class Entry {
                public static object Test() => nameof(TypeAccessor.Create);
            }
            """
        );
        GeneratorTestHelper.AssertCompiles(result);
        Assert.AreEqual(0, result.Driver.GetRunResult().GeneratedTrees.Length);
        Assert.AreEqual("Create", GeneratorTestHelper.Execute(result));
    }

    [TestMethod]
    public async Task UnrelatedAndLegacyNamedFactoriesAreNotMatched()
    {
        var result = await GeneratorTestHelper.Run(
            """
            namespace HKW.FastMember {
                public static class TypeAccessor { public static object Create<T>() => new object(); }
            }
            public static class Other { public static object Create<T>() => new object(); }
            public static class Entry {
                public static object Test() {
                    System.Func<object> factory = Other.Create<object>;
                    var old = HKW.FastMember.TypeAccessor.Create<object>();
                    return factory();
                }
            }
            """
        );
        GeneratorTestHelper.AssertCompiles(result);
        Assert.AreEqual(0, result.Diagnostics.Length);
        Assert.AreEqual(0, result.Driver.GetRunResult().GeneratedTrees.Length);
    }

    [TestMethod]
    public async Task ExternAliasOnlyModelIsDiagnosedInsteadOfEmittingUnbindableCode()
    {
        var external = GeneratorTestHelper.Compilation(
            "namespace Models { public class Model { public int Id; } }"
        );
        var reference = MetadataReference.CreateFromImage(
            GeneratorTestHelper.Emit(external),
            new MetadataReferenceProperties(aliases: ["other"])
        );
        var result = await GeneratorTestHelper.Run(
            """
            extern alias other;
            using HKW.FastMember.Generater;
            public static class Entry { public static object Test() => TypeAccessor.Create<other::Models.Model>(); }
            """,
            extra: [reference]
        );
        Assert.AreEqual(1, result.Diagnostics.Length);
        Assert.AreEqual("HFMG008", result.Diagnostics[0].Id);
        Assert.AreEqual(0, result.Driver.GetRunResult().GeneratedTrees.Length);
    }

    [TestMethod]
    public async Task ExplicitDiagnosticConfigurationSurvivesGeneralAnalyzerDisable()
    {
        var configuration = new GeneratorTestHelper.Configuration(
            new Dictionary<string, string>
            {
                ["dotnet_analyzer_diagnostic.severity"] = "none",
                ["dotnet_diagnostic.HFMG004.severity"] = "error",
                ["dotnet_diagnostic.HFMG001.severity"] = "error",
            }
        );
        var result = await GeneratorTestHelper.Run(
            """
            using HKW.FastMember.Generater;
            public class Model { }
            public static class Entry {
                public static object Test() {
                    System.Func<global::HKW.FastMember.Abstractions.TypeAccessor<Model>> factory = TypeAccessor.Create<Model>;
                    return TypeAccessor.Create<object>();
                }
            }
            """,
            options: configuration
        );
        CollectionAssert.AreEquivalent(
            new[] { "HFMG001", "HFMG004" },
            result.Diagnostics.Select(d => d.Id).ToArray()
        );
        Assert.IsTrue(result.Diagnostics.All(d => d.Severity == DiagnosticSeverity.Error));
    }
}
