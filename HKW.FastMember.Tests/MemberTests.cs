using System.Reflection;
using HKW.FastMember;
using HKW.FastMember.Abstractions;

namespace HKW.FastMember.Tests;

[TestClass]
public sealed class MemberTests
{
    [TestMethod]
    public void Metadata_DescribesFieldsAndProperties()
    {
        var members = TypeHelpers.GetMembers<MemberModel>().ToDictionary(member => member.Name);

        var property = members[nameof(MemberModel.Name)];
        Assert.AreEqual(nameof(MemberModel.Name), property.Name);
        Assert.AreEqual(typeof(string), property.Type);
        Assert.IsInstanceOfType<PropertyInfo>(property.MemberInfo);
        Assert.IsTrue(property.CanRead);
        Assert.IsTrue(property.CanWrite);

        var field = members[nameof(MemberModel.Number)];
        Assert.AreEqual(nameof(MemberModel.Number), field.Name);
        Assert.AreEqual(typeof(int), field.Type);
        Assert.IsInstanceOfType<FieldInfo>(field.MemberInfo);
    }

    [TestMethod]
    public void PropertyCapabilities_ReflectAvailableAccessors()
    {
        var members = TypeHelpers.GetMembers<MemberModel>().ToDictionary(member => member.Name);

        Assert.IsTrue(members[nameof(MemberModel.ReadOnlyProperty)].CanRead);
        Assert.IsFalse(members[nameof(MemberModel.ReadOnlyProperty)].CanWrite);
        Assert.IsFalse(members[nameof(MemberModel.WriteOnlyProperty)].CanRead);
        Assert.IsTrue(members[nameof(MemberModel.WriteOnlyProperty)].CanWrite);

        var field = members[nameof(MemberModel.Number)];
        Assert.IsTrue(field.CanRead);
        Assert.IsTrue(field.CanWrite);
        Assert.IsFalse(members[nameof(MemberModel.ReadOnlyField)].CanWrite);
    }

    [TestMethod]
    public void OrdinalAndAttributeHelpers_ReportConfiguredAndDefaultValues()
    {
        var members = TypeHelpers.GetMembers<OrdinalModel>().ToDictionary(member => member.Name);
        var first = members[nameof(OrdinalModel.First)];

        Assert.AreEqual(0, first.Ordinal);
        Assert.IsTrue(first.IsDefined<OrdinalAttribute>());
        Assert.IsTrue(first.IsDefined(typeof(OrdinalAttribute)));
        Assert.AreEqual(
            (ushort)0,
            ((OrdinalAttribute)first.GetAttribute<OrdinalAttribute>()!).Ordinal
        );
        Assert.AreEqual(
            (ushort)0,
            ((OrdinalAttribute)first.GetAttribute(typeof(OrdinalAttribute))!).Ordinal
        );

        var unconfigured = TypeHelpers
            .GetMembers<MemberModel>()
            .Single(member => member.Name == nameof(MemberModel.Name));
        Assert.AreEqual(-1, unconfigured.Ordinal);
        Assert.IsFalse(unconfigured.IsDefined<OrdinalAttribute>());
        Assert.IsNull(unconfigured.GetAttribute<OrdinalAttribute>());
    }

    [TestMethod]
    public void OrdinalOrdering_PutsDefaultsFirstAndPreservesDuplicateOrder()
    {
        var ordered = TypeHelpers
            .GetMembers<MixedOrdinalModel>()
            .OrderBy(member => member.Ordinal)
            .Select(member => member.Name)
            .ToArray();

        CollectionAssert.AreEqual(
            new[]
            {
                nameof(MixedOrdinalModel.DefaultOrdinal),
                nameof(MixedOrdinalModel.Alpha),
                nameof(MixedOrdinalModel.Omega),
            },
            ordered
        );
    }

    [TestMethod]
    public void EqualityAndString_UseReferenceIdentityAndDefaultTypeText()
    {
        var first = TypeHelpers
            .GetMembers<MemberModel>()
            .Single(member => member.Name == nameof(MemberModel.Name));
        var second = TypeHelpers
            .GetMembers<MemberModel>()
            .Single(member => member.Name == nameof(MemberModel.Name));

        Assert.AreSame(first.MemberInfo, second.MemberInfo);
        Assert.IsFalse(first.Equals(second));
        Assert.IsTrue(first.Equals(first));
        Assert.AreEqual(typeof(ReflectionMember).FullName, first.ToString());
        Member common = first;
        Assert.AreSame(first.MemberInfo, common.GetMemberInfo());
    }
}
