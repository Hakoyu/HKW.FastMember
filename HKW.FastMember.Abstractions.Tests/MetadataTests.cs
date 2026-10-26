using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using static HKW.FastMember.Abstractions.Tests.AccessorContractTests;

namespace HKW.FastMember.Abstractions.Tests;

[TestClass]
public sealed class MetadataTests
{
    [TestMethod]
    public void DescriptorsAreImmutableAndExtensible()
    {
        var member = new SpecializedMember();
        Assert.AreEqual("Value", member.Name);
        Assert.AreEqual(typeof(int?), member.Type);
        Assert.AreEqual(12, member.Ordinal);
        Assert.IsTrue(member.CanRead);
        Assert.IsFalse(member.CanWrite);
        Assert.AreEqual("Model.Value", member.DeclaringIdentity);
        Assert.AreEqual(typeof(int), member.EffectiveType);
        Assert.IsTrue(member.AllowDBNull);
        foreach (var property in typeof(Member).GetProperties())
        {
            Assert.IsNull(property.SetMethod);
            Assert.IsFalse(property.GetMethod!.IsVirtual);
        }
        Assert.IsFalse(typeof(Member).IsSealed);
        Assert.IsNull(typeof(Member).GetProperty("MemberInfo"));
        Assert.IsNull(
            typeof(Member).GetMethod(
                "HasSameContract",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
            )
        );
    }

    [TestMethod]
    public void FrozenMemberConstructorAndDamFlow()
    {
        var constructor = typeof(Member).GetConstructor([
            typeof(string),
            typeof(Type),
            typeof(int),
            typeof(bool),
            typeof(bool),
            typeof(string),
            typeof(Type),
            typeof(bool),
        ])!;
        CollectionAssert.AreEqual(
            new[]
            {
                "name",
                "type",
                "ordinal",
                "canRead",
                "canWrite",
                "declaringIdentity",
                "effectiveType",
                "allowDBNull",
            },
            constructor.GetParameters().Select(p => p.Name).ToArray()
        );
        const DynamicallyAccessedMemberTypes expected =
            DynamicallyAccessedMemberTypes.PublicFields
            | DynamicallyAccessedMemberTypes.PublicProperties;
        Assert.AreEqual(
            expected,
            constructor
                .GetParameters()[6]
                .GetCustomAttribute<DynamicallyAccessedMembersAttribute>()!
                .MemberTypes
        );
        Assert.AreEqual(
            expected,
            typeof(Member)
                .GetProperty("EffectiveType")!
                .GetCustomAttribute<DynamicallyAccessedMembersAttribute>()!
                .MemberTypes
        );
    }

    [TestMethod]
    [DataRow("name")]
    [DataRow("type")]
    [DataRow("declaringIdentity")]
    [DataRow("effectiveType")]
    public void DescriptorNullArgumentsAreRejected(string parameter)
    {
        var exception = Assert.ThrowsExactly<ArgumentNullException>(() =>
            new Member(
                parameter == "name" ? null! : "Value",
                parameter == "type" ? null! : typeof(int),
                -1,
                true,
                true,
                parameter == "declaringIdentity" ? null! : "Model.Value",
                parameter == "effectiveType" ? null! : typeof(int),
                false
            )
        );
        Assert.AreEqual(parameter, exception.ParamName);
    }

    [TestMethod]
    public void DescriptorsAreSnapshottedAndCollectionsAreReadOnly()
    {
        Member[] input = [Describe("A"), Describe("B")];
        var first = input[0];
        var accessor = new BareAccessor(input, version: 7);
        input[0] = Describe("Z");
        Assert.AreSame(first, accessor.GetMembers()[0]);
        Assert.AreSame(first, accessor.GetMemberDictionary()["A"]);
        Assert.AreEqual(accessor.GetMembers(), accessor.GetMembers());
        Assert.AreSame(accessor.GetMemberDictionary(), accessor.GetMemberDictionary());
        Assert.ThrowsExactly<NotSupportedException>(() =>
            ((IList<Member>)accessor.GetMembers())[0] = Describe("X")
        );
        Assert.ThrowsExactly<NotSupportedException>(() =>
            ((IDictionary<string, Member>)accessor.GetMemberDictionary()).Add("X", Describe("X"))
        );
        Assert.IsFalse(accessor.GetMemberDictionary().ContainsKey("a"));
        Assert.AreEqual(7, accessor.ContractVersion);
        Assert.IsTrue(accessor.GetMembersSupported);
    }

    [TestMethod]
    public void ConstructorRejectsNullMetadataAndTargetType()
    {
        Assert.AreEqual(
            "members",
            Assert.ThrowsExactly<ArgumentNullException>(() => new BareAccessor(null!)).ParamName
        );
        Assert.ThrowsExactly<ArgumentNullException>(() => new BareAccessor([null!]));
        Assert.AreEqual(
            "targetType",
            Assert.ThrowsExactly<ArgumentNullException>(() => new TargetAccessor(null!)).ParamName
        );
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ConstructorRejectsDuplicatesAndUnsortedNames(bool duplicate)
    {
        Assert.AreEqual(
            "members",
            Assert
                .ThrowsExactly<ArgumentException>(() =>
                    new BareAccessor(
                        duplicate ? [Describe("A"), Describe("A")] : [Describe("B"), Describe("A")]
                    )
                )
                .ParamName
        );
    }

    [TestMethod]
    public void ConstructorAcceptsEmptyMetadataAndOrdinalCaseDistinctions()
    {
        Assert.AreEqual(0, new BareAccessor([]).GetMembers().Length);
        var accessor = new BareAccessor([Describe("A"), Describe("a")]);
        Assert.AreEqual(2, accessor.GetMemberDictionary().Count);
        Assert.AreEqual(1, accessor.ContractVersion);
    }

    [TestMethod]
    public void OrdinalAttributeRetainsValueAndDeclarationOnlyUsage()
    {
        var attribute = new OrdinalAttribute(-7);
        Assert.AreEqual(-7, attribute.Ordinal);
        Assert.IsNull(typeof(OrdinalAttribute).GetProperty("Ordinal")!.SetMethod);
        var usage = typeof(OrdinalAttribute).GetCustomAttribute<AttributeUsageAttribute>()!;
        Assert.AreEqual(AttributeTargets.Property | AttributeTargets.Field, usage.ValidOn);
        Assert.IsFalse(usage.AllowMultiple);
        Assert.IsFalse(usage.Inherited);
    }

    [TestMethod]
    public void CommonAssemblyHasNoBackendCompilerOrDynamicDependency()
    {
        var references = typeof(TypeAccessor)
            .Assembly.GetReferencedAssemblies()
            .Select(a => a.Name!)
            .ToArray();
        Assert.IsFalse(
            references.Any(name =>
                name.StartsWith("HKW.FastMember", StringComparison.Ordinal)
                || name.Contains("CodeAnalysis", StringComparison.Ordinal)
                || name.Contains("SourceGeneratorUtils", StringComparison.Ordinal)
                || name == "Microsoft.CSharp"
                || name.Contains("Reflection.Emit", StringComparison.Ordinal)
            )
        );
        Assert.AreEqual(
            0,
            typeof(TypeAccessor).GetMethods(BindingFlags.Public | BindingFlags.Static).Length
        );
        Assert.IsNull(
            typeof(TypeAccessor).GetMethod(
                "HasSameContract",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
            )
        );
        foreach (
            var type in new[]
            {
                typeof(TypeAccessor),
                typeof(TypeAccessor<>),
                typeof(ObjectAccessor),
                typeof(ObjectAccessor<>),
                typeof(Member),
            }
        )
        {
            Assert.IsNull(type.GetCustomAttribute<RequiresDynamicCodeAttribute>());
            Assert.IsNull(type.GetCustomAttribute<RequiresUnreferencedCodeAttribute>());
            foreach (
                var method in type.GetMethods(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly
                )
            )
            {
                Assert.IsNull(method.GetCustomAttribute<RequiresDynamicCodeAttribute>());
                Assert.IsNull(method.GetCustomAttribute<RequiresUnreferencedCodeAttribute>());
            }
        }
    }

    private sealed class SpecializedMember()
        : Member("Value", typeof(int?), 12, true, false, "Model.Value", typeof(int), true);

    private sealed class TargetAccessor(Type targetType) : TypeAccessor(targetType, [], false);
}
