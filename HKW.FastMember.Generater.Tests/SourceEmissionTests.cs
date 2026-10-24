using System.Globalization;
using HKW.SourceGeneratorUtils;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace HKW.FastMember.Generater.Tests;

[TestClass]
public sealed class SourceEmissionTests
{
    [TestMethod]
    public async Task StructuredOutputIsIndentedDeterministicAndCultureIndependent()
    {
        const string source = """
            using HKW.FastMember.Generater;
            public class Model {
                [global::HKW.FastMember.Abstractions.Ordinal(-12)] public string? @class { get; set; }
            }
            public static class Entry {
                public static object Test() {
                    var accessor = TypeAccessor.Create<Model>();
                    var model = accessor.CreateNew();
                    accessor[model, "class"] = "value";
                    return accessor[model, "class"]!;
                }
            }
            """;
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            var first = await GeneratorTestHelper.Run(source, "CultureConsumer");
            var custom = (CultureInfo)CultureInfo.InvariantCulture.Clone();
            custom.NumberFormat.NegativeSign = "~";
            CultureInfo.CurrentCulture = custom;
            var second = await GeneratorTestHelper.Run(source, "CultureConsumer");
            Assert.AreEqual("value", GeneratorTestHelper.Execute(first));
            Assert.AreEqual("value", GeneratorTestHelper.Execute(second));
            var firstOutput = first
                .Driver.GetRunResult()
                .Results.Single()
                .GeneratedSources.Single();
            var secondOutput = second
                .Driver.GetRunResult()
                .Results.Single()
                .GeneratedSources.Single();
            var text = firstOutput.SourceText.ToString();
            Assert.AreEqual(firstOutput.HintName, secondOutput.HintName);
            Assert.AreEqual(text, secondOutput.SourceText.ToString());
            Assert.IsFalse(text.Contains('\r'));
            StringAssert.Contains(text, "        protected override object? GetValueTypedCore");
            StringAssert.Contains(
                text,
                "            switch (index)\n            {\n                case 0:"
            );
            StringAssert.Contains(text, ").@class");
            Assert.IsFalse(text.Contains("HKW.SourceGeneratorUtils", StringComparison.Ordinal));
            var classes = firstOutput
                .SyntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<ClassDeclarationSyntax>()
                .ToArray();
            Assert.AreEqual(2, classes.Length);
            Assert.IsTrue(classes[0].Modifiers.Any(SyntaxKind.SealedKeyword));
            Assert.IsTrue(classes[1].Modifiers.Any(SyntaxKind.StaticKeyword));
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [TestMethod]
    [DoNotParallelize]
    public async Task SharedUtilityDefaultsCannotChangeGeneratedContract()
    {
        var methodDefaults = MethodGenerateInfo.DefaultAttributes;
        var objectDefaults = ObjectGenerateInfo.DefaultAttributes;
        try
        {
            AttributeGenerateInfo[] defaults =
            [
                new("[global::System.Obsolete(\"foreign generator default\", true)]"),
            ];
            MethodGenerateInfo.DefaultAttributes = defaults;
            ObjectGenerateInfo.DefaultAttributes = defaults;
            var result = await GeneratorTestHelper.Run(
                """
                using HKW.FastMember.Generater;
                public class Model { public int Value { get; set; } }
                public static class Entry {
                    public static object Test() {
                        var accessor = TypeAccessor.Create<Model>();
                        return accessor.CreateNew().Value;
                    }
                }
                """
            );
            Assert.AreEqual(0, GeneratorTestHelper.Execute(result));
            var text = result.Driver.GetRunResult().GeneratedTrees.Single().GetText().ToString();
            Assert.IsFalse(text.Contains("foreign generator default", StringComparison.Ordinal));
        }
        finally
        {
            MethodGenerateInfo.DefaultAttributes = methodDefaults;
            ObjectGenerateInfo.DefaultAttributes = objectDefaults;
        }
    }

    [TestMethod]
    [DataRow("", 0)]
    [DataRow("public int Value { get; }", 1)]
    [DataRow("public int Value { set { } }", 1)]
    [DataRow("public int Value { get; set; }", 1)]
    public async Task EmptyAndOneSidedDispatchCompileWithoutRuntimeUtilityDependency(
        string member,
        int count
    )
    {
        var result = await GeneratorTestHelper.Run(
            """
            using HKW.FastMember.Generater;
            public class Model { MEMBER }
            public static class Entry {
                public static object Test() {
                    var accessor = TypeAccessor.Create<Model>();
                    foreach (var assembly in typeof(Entry).Assembly.GetReferencedAssemblies())
                        if (assembly.Name == "HKW.SourceGeneratorUtils")
                            throw new System.InvalidOperationException("Generator utility leaked into consumer runtime");
                    return accessor.GetMembers().Count;
                }
            }
            """.Replace("MEMBER", member)
        );
        Assert.AreEqual(count, GeneratorTestHelper.Execute(result));
        Assert.IsNull(result.Driver.GetRunResult().Results.Single().Exception);
    }
}
