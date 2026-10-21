using System.Reflection;
using HKW.FastMember;

namespace HKW.FastMemberTests;

[TestClass]
public sealed class TypeHelpersTests
{
    private const BindingFlags PublicInstance = BindingFlags.Public | BindingFlags.Instance;

    [TestMethod]
    public void GetTypeAndInterfaceProperties_ReturnsClassAndInheritedInterfaceProperties()
    {
        var classProperties = typeof(MemberModel)
            .GetTypeAndInterfaceProperties(PublicInstance)
            .Select(property => property.Name)
            .ToArray();
        var interfaceProperties = typeof(ICombinedContract)
            .GetTypeAndInterfaceProperties(PublicInstance)
            .Select(property => property.Name)
            .ToArray();

        CollectionAssert.Contains(classProperties, nameof(MemberModel.Name));
        CollectionAssert.Contains(classProperties, "Item");
        CollectionAssert.AreEquivalent(
            new[]
            {
                nameof(ILeftContract.Shared),
                nameof(IRightContract.Shared),
                nameof(ICombinedContract.Enabled),
            },
            interfaceProperties
        );
    }

    [TestMethod]
    public void GetMembers_DefaultsToSortedPublicInstanceNonIndexerMembers()
    {
        var names = TypeHelpers.GetMembers<MemberModel>().Select(member => member.Name).ToArray();

        CollectionAssert.AreEqual(
            new[]
            {
                nameof(MemberModel.IndexedValue),
                nameof(MemberModel.Name),
                nameof(MemberModel.Number),
                nameof(MemberModel.ReadOnlyField),
                nameof(MemberModel.ReadOnlyProperty),
                nameof(MemberModel.WriteOnlyProperty),
            },
            names
        );
        CollectionAssert.DoesNotContain(names, "Item");
        CollectionAssert.DoesNotContain(names, nameof(MemberModel.StaticProperty));
        CollectionAssert.DoesNotContain(names, "PrivateProperty");
    }

    [TestMethod]
    public void TypeAccessorMetadata_MatchesItsAccessibleMemberSurface()
    {
        var accessor = TypeAccessor.Create(typeof(MemberModel));
        var names = accessor.GetMembers().Select(member => member.Name).ToArray();

        CollectionAssert.DoesNotContain(names, "Item");
        CollectionAssert.DoesNotContain(names, nameof(MemberModel.StaticField));
        CollectionAssert.DoesNotContain(names, "PrivateField");
        Assert.IsFalse(accessor.TryGetValue(new MemberModel(), "Item", out _));
    }

    [TestMethod]
    public void GetMembers_DiscoversInheritedClassAndInterfaceMembers()
    {
        var classNames = TypeHelpers.GetMembers<DerivedModel>()
            .Select(member => member.Name)
            .ToArray();
        var interfaceNames = TypeHelpers.GetMembers<IDerivedContract>()
            .Select(member => member.Name)
            .ToArray();

        CollectionAssert.AreEqual(
            new[] { nameof(BaseModel.BaseValue), nameof(DerivedModel.DerivedValue) },
            classNames
        );
        CollectionAssert.AreEqual(
            new[] { nameof(IBaseContract.Name), nameof(IDerivedContract.Number) },
            interfaceNames
        );
    }

    [TestMethod]
    public void GetMembers_DeduplicatesHiddenAndRepeatedInterfaceNames()
    {
        var hidden = TypeHelpers.GetMembers<HiddenDerivedModel>()
            .Where(member => member.Name == nameof(HiddenDerivedModel.Value))
            .ToArray();
        var combined = TypeHelpers.GetMembers<ICombinedContract>();

        Assert.HasCount(1, hidden);
        Assert.AreEqual(typeof(HiddenDerivedModel), hidden[0].MemberInfo.DeclaringType);
        Assert.HasCount(1, combined.Where(member => member.Name == nameof(ILeftContract.Shared)));
        CollectionAssert.AreEquivalent(
            new[] { nameof(ICombinedContract.Enabled), nameof(ILeftContract.Shared) },
            combined.Select(member => member.Name).ToArray()
        );
    }

    [TestMethod]
    public void BindingFlags_AreForwardedForStaticAndNonPublicMembers()
    {
        var staticNames = TypeHelpers
            .GetMembers<MemberModel>(BindingFlags.Public | BindingFlags.Static)
            .Select(member => member.Name)
            .ToArray();
        var nonPublicNames = TypeHelpers
            .GetMembers<MemberModel>(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(member => member.Name)
            .ToArray();

        CollectionAssert.AreEquivalent(
            new[] { nameof(MemberModel.StaticField), nameof(MemberModel.StaticProperty) },
            staticNames
        );
        CollectionAssert.Contains(nonPublicNames, "PrivateProperty");
        CollectionAssert.Contains(nonPublicNames, "PrivateField");
        CollectionAssert.Contains(nonPublicNames, "_privateField");
    }

    [TestMethod]
    public void DictionaryOverloads_ForwardFlagsDeduplicateAndUseOrdinalKeys()
    {
        var generic = TypeHelpers.GetMembersDictionary<MemberModel>(
            BindingFlags.Public | BindingFlags.Static
        );
        var combined = typeof(ICombinedContract).GetMembersDictionary(PublicInstance);

        CollectionAssert.AreEquivalent(
            new[] { nameof(MemberModel.StaticField), nameof(MemberModel.StaticProperty) },
            generic.Keys.ToArray()
        );
        Assert.AreEqual(1, combined.Keys.Count(name => name == nameof(ILeftContract.Shared)));
        Assert.IsTrue(combined.ContainsKey(nameof(ICombinedContract.Enabled)));
        Assert.IsFalse(combined.ContainsKey(nameof(ICombinedContract.Enabled).ToLowerInvariant()));
    }

    [TestMethod]
    public void NullType_ThrowsWithTypeParameter()
    {
        var membersError = Assert.ThrowsExactly<ArgumentNullException>(() =>
            TypeHelpers.GetMembers(null!)
        );
        var dictionaryError = Assert.ThrowsExactly<ArgumentNullException>(() =>
            TypeHelpers.GetMembersDictionary(null!)
        );

        Assert.AreEqual("type", membersError.ParamName);
        Assert.AreEqual("type", dictionaryError.ParamName);
    }
}
