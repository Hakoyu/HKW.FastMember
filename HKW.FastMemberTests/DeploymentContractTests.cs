using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using HKW.FastMember;

namespace HKW.FastMemberTests;

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

        Assert.HasCount(7, factories);
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
    public void ReaderConstructor_DeclaresRequirementsForDerivedReaders()
    {
        var constructor = typeof(ObjectReader).GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance).Single();
        Assert.IsNotNull(constructor.GetCustomAttribute<RequiresDynamicCodeAttribute>());
        Assert.IsNotNull(constructor.GetCustomAttribute<RequiresUnreferencedCodeAttribute>());
    }

    [TestMethod]
    public void MetadataHelpers_DeclareTrimmingButNotDynamicCodeRequirements()
    {
        var helpers = typeof(TypeHelpers).GetMethods(BindingFlags.Public | BindingFlags.Static);
        Assert.HasCount(5, helpers);
        foreach (var helper in helpers)
        {
            Assert.IsNotNull(helper.GetCustomAttribute<RequiresUnreferencedCodeAttribute>(), helper.ToString());
            Assert.IsNull(helper.GetCustomAttribute<RequiresDynamicCodeAttribute>(), helper.ToString());
        }
    }
}
