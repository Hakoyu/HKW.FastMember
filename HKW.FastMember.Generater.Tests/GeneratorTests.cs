using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace HKW.FastMember.Generater.Tests;

[TestClass]
public sealed class GeneratorTests
{
    [TestMethod]
    [DataRow("TypeAccessor.Create<Model>()", "using HKW.FastMember.Generater;")]
    [DataRow("A.Create<Model>()", "using A = HKW.FastMember.Generater.TypeAccessor;")]
    [DataRow("global::HKW.FastMember.Generater.TypeAccessor.Create<Model>()", "")]
    [DataRow("Create<Model>()", "using static HKW.FastMember.Generater.TypeAccessor;")]
    public async Task EntrySpellingsCompileAndRun(string expression, string imports)
    {
        var result = await GeneratorTestHelper.Run(
            imports
                + """

                public class Model { public int Id { get; set; } }
                public static class Entry {
                    public static object Test() {
                        var accessor = EXPRESSION;
                        var model = new Model();
                        accessor[model, "Id"] = 17;
                        return accessor[model, "Id"]!;
                    }
                }
                """.Replace("EXPRESSION", expression)
        );
        Assert.AreEqual(17, GeneratorTestHelper.Execute(result));
        Assert.AreEqual(1, result.Driver.GetRunResult().GeneratedTrees.Length);
    }

    [TestMethod]
    [DataRow("ObjectAccessor.Create(new Model())")]
    [DataRow("ObjectReader.Create(new Model[0])")]
    [DataRow("ObjectReader.Create(Items())")]
    [DataRow("ObjectReader.Create(Items(), default(System.Threading.CancellationToken))")]
    public async Task EveryFactoryIndependentlyGeneratesAnAccessor(string expression)
    {
        var result = await GeneratorTestHelper.Run(
            """
            using HKW.FastMember.Generater;
            public class Model { public int Id { get; set; } }
            public static class Entry {
                public static object Test() {
                    var value = EXPRESSION;
                    return GeneratedAccessorRegistry<Model>.GetAccessor().GetMembers().Count;
                }
                private static async System.Collections.Generic.IAsyncEnumerable<Model> Items() {
                    await System.Threading.Tasks.Task.CompletedTask;
                    yield break;
                }
            }
            """.Replace("EXPRESSION", expression)
        );
        Assert.AreEqual(1, GeneratorTestHelper.Execute(result));
        Assert.AreEqual(1, result.Driver.GetRunResult().GeneratedTrees.Length);
    }

    [TestMethod]
    public async Task RepeatedCallsNestedFunctionsAndNullableAnnotationsDeduplicate()
    {
        var result = await GeneratorTestHelper.Run(
            """
            using HKW.FastMember.Generater;
            public class Model { public string? Name { get; set; } }
            public static class Entry {
                public static object Test() {
                    var first = TypeAccessor.Create<Model>();
                    global::HKW.FastMember.Abstractions.TypeAccessor<Model> Local() => TypeAccessor.Create<Model>();
                    Model? item = new Model();
                    var bound = ObjectAccessor.Create(item);
                    return object.ReferenceEquals(first, Local());
                }
            }
            """
        );
        Assert.AreEqual(true, GeneratorTestHelper.Execute(result));
        Assert.AreEqual(1, result.Driver.GetRunResult().GeneratedTrees.Length);
    }

    [TestMethod]
    public async Task NestedClosedGenericTupleAndDynamicValuesCompile()
    {
        var result = await GeneratorTestHelper.Run(
            """
            using HKW.FastMember.Generater;
            public class Outer<T> { public class Inner<U> {
                public T? First { get; set; }
                public U? Second { get; set; }
                public (int Id, string Name) Pair { get; set; }
                public dynamic? Data { get; set; }
            } }
            public static class Entry {
                public static object Test() {
                    var accessor = TypeAccessor.Create<Outer<string>.Inner<int>>();
                    var value = accessor.CreateNew();
                    accessor[value, "First"] = "nested";
                    accessor[value, "Second"] = 9;
                    accessor[value, "Data"] = new object();
                    return accessor[value, "Second"]!;
                }
            }
            """
        );
        Assert.AreEqual(9, GeneratorTestHelper.Execute(result));
    }

    [TestMethod]
    public void IncrementalEditKeepsUnchangedAccessorCached()
    {
        const string source = """
            using HKW.FastMember.Generater;
            public class One { public int Id { get; set; } }
            public class Two { public int Id { get; set; } }
            public static class Entry {
                public static object Test() => new object[] { TypeAccessor.Create<One>(), TypeAccessor.Create<Two>() };
            }
            """;
        var compilation = GeneratorTestHelper.Compilation(source);
        var driver = GeneratorTestHelper.Driver().RunGenerators(compilation);
        var before = driver
            .GetRunResult()
            .Results.Single()
            .GeneratedSources.ToDictionary(s => s.HintName, s => s.SourceText.ToString());
        var tree = CSharpSyntaxTree.ParseText(
            source.Replace("class One { public int Id", "class One { public long Id"),
            GeneratorTestHelper.ParseOptions,
            "Consumer.cs"
        );
        driver = driver.RunGenerators(
            compilation.ReplaceSyntaxTree(compilation.SyntaxTrees.Single(), tree)
        );
        var after = driver.GetRunResult().Results.Single().GeneratedSources;
        Assert.AreEqual(2, after.Length);
        Assert.AreEqual(1, after.Count(s => before[s.HintName] == s.SourceText.ToString()));
        var reasons = driver
            .GetRunResult()
            .Results.Single()
            .TrackedSteps["AccessorModels"]
            .SelectMany(s => s.Outputs)
            .Select(o => o.Reason)
            .ToArray();
        Assert.IsTrue(
            reasons.Any(r =>
                r == IncrementalStepRunReason.Cached || r == IncrementalStepRunReason.Unchanged
            )
        );
    }

    [TestMethod]
    public async Task MissingGeneratorNeverFallsBack()
    {
        var compilation = GeneratorTestHelper.Compilation(
            """
            using HKW.FastMember.Generater;
            public class NoGeneration { public int Id { get; set; } }
            public static class Entry {
                public static object Test() {
                    try { TypeAccessor.Create<NoGeneration>(); return false; }
                    catch (System.InvalidOperationException error) {
                        return error.Message.Contains("NoGeneration") && error.Message.Contains("source generator");
                    }
                }
            }
            """
        );
        var result = new GeneratorTestHelper.Result(
            compilation,
            GeneratorTestHelper.Driver(),
            [],
            []
        );
        Assert.AreEqual(true, GeneratorTestHelper.Execute(result));
        await Task.CompletedTask;
    }

    [TestMethod]
    public async Task OrdinalDoesNotLeakFromHiddenOrOverriddenDeclaration()
    {
        var result = await GeneratorTestHelper.Run(
            """
            using HKW.FastMember.Generater;
            public class Parent {
                [global::HKW.FastMember.Abstractions.Ordinal(5)] public virtual int Value { get; set; }
            }
            public class Model : Parent { public override int Value { get; set; } }
            public static class Entry {
                public static object Test() => TypeAccessor.Create<Model>().GetMemberDictionary()["Value"].Ordinal;
            }
            """
        );
        Assert.AreEqual(-1, GeneratorTestHelper.Execute(result));
    }
}
