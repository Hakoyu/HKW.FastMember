using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using HKW.FastMember;

namespace HKW.FastMember.Tests;

[TestClass]
public sealed class DeploymentContractTests
{
    [TestMethod]
    public void BuiltInFactories_DeclareDynamicCodeAndTrimmingRequirements()
    {
        var factories = new[] { typeof(TypeAccessor), typeof(ObjectAccessor), typeof(ObjectReader) }
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Static))
            .Where(method => method.Name == "Create")
            .ToArray();

        Assert.HasCount(9, factories);
        Assert.IsTrue(
            factories.All(factory =>
                factory.ReturnType.Assembly
                == typeof(HKW.FastMember.Abstractions.TypeAccessor).Assembly
            )
        );
        foreach (var factory in factories)
        {
            var dynamicCode = factory.GetCustomAttribute<RequiresDynamicCodeAttribute>();
            var trimming = factory.GetCustomAttribute<RequiresUnreferencedCodeAttribute>();
            Assert.IsNotNull(dynamicCode, factory.ToString());
            Assert.IsNotNull(trimming, factory.ToString());
            Assert.IsTrue(dynamicCode.Message.Contains("Native AOT", StringComparison.Ordinal));
            Assert.IsTrue(trimming.Message.Contains("trimming", StringComparison.Ordinal));
        }
    }

    [TestMethod]
    public void ReaderFactory_ReturnsSharedReaderWithoutPollutingItsDeploymentContract()
    {
        Assert.IsTrue(typeof(ObjectReader).IsAbstract && typeof(ObjectReader).IsSealed);
        Assert.HasCount(
            0,
            typeof(ObjectReader).GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance)
        );
        var common = typeof(HKW.FastMember.Abstractions.ObjectReader);
        foreach (
            var factory in common
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(m => m.Name == "Create")
        )
        {
            Assert.IsNull(factory.GetCustomAttribute<RequiresDynamicCodeAttribute>());
            Assert.IsNull(factory.GetCustomAttribute<RequiresUnreferencedCodeAttribute>());
        }
        using var reader = ObjectReader.Create(Array.Empty<MemberModel>());
        Assert.IsInstanceOfType(reader, common);
    }

    [TestMethod]
    public void MetadataHelpers_DeclareTrimmingButNotDynamicCodeRequirements()
    {
        var helpers = typeof(TypeHelpers).GetMethods(BindingFlags.Public | BindingFlags.Static);
        Assert.HasCount(5, helpers);
        foreach (var helper in helpers)
        {
            Assert.IsNotNull(
                helper.GetCustomAttribute<RequiresUnreferencedCodeAttribute>(),
                helper.ToString()
            );
            Assert.IsNull(
                helper.GetCustomAttribute<RequiresDynamicCodeAttribute>(),
                helper.ToString()
            );
        }
    }
}
