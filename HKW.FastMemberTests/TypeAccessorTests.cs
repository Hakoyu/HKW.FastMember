using System.Reflection;
using HKW.FastMember;
using Microsoft.CSharp.RuntimeBinder;

namespace HKW.FastMemberTests;

[TestClass]
public sealed class TypeAccessorTests
{
    [TestMethod]
    public void Indexer_GetsAndSetsPublicPropertiesAndFields()
    {
        var accessor = TypeAccessor.Create(typeof(MemberModel));
        var model = new MemberModel();

        accessor[model, nameof(MemberModel.Name)] = "updated";
        accessor[model, nameof(MemberModel.Number)] = 42;

        Assert.AreEqual("updated", accessor[model, nameof(MemberModel.Name)]);
        Assert.AreEqual(42, accessor[model, nameof(MemberModel.Number)]);
        Assert.AreEqual("updated", model.Name);
        Assert.AreEqual(42, model.Number);
    }

    [TestMethod]
    public void Indexer_SupportsNullForNullableAndReferenceMembers()
    {
        var accessor = TypeAccessor.Create(typeof(NullableModel));
        var model = new NullableModel { NullableNumber = 1, NullableText = "value" };

        accessor[model, nameof(NullableModel.NullableNumber)] = null!;
        accessor[model, nameof(NullableModel.NullableText)] = null!;

        Assert.IsNull(accessor[model, nameof(NullableModel.NullableNumber)]);
        Assert.IsNull(accessor[model, nameof(NullableModel.NullableText)]);
    }

    [TestMethod]
    public void CreateNew_CreatesIndependentInstancesWhenPublicParameterlessConstructorExists()
    {
        var accessor = TypeAccessor.Create(typeof(MemberModel));

        Assert.IsTrue(accessor.CreateNewSupported);
        var first = accessor.CreateNew();
        var second = accessor.CreateNew();

        Assert.IsInstanceOfType<MemberModel>(first);
        Assert.IsInstanceOfType<MemberModel>(second);
        Assert.AreNotSame(first, second);
    }

    [TestMethod]
    public void InheritedClassMembers_AreAccessible()
    {
        var accessor = TypeAccessor.Create(typeof(DerivedModel));
        var model = new DerivedModel();

        accessor[model, nameof(BaseModel.BaseValue)] = "base";
        accessor[model, nameof(DerivedModel.DerivedValue)] = 7;

        Assert.AreEqual("base", accessor[model, nameof(BaseModel.BaseValue)]);
        Assert.AreEqual(7, accessor[model, nameof(DerivedModel.DerivedValue)]);
    }

    [TestMethod]
    public void InheritedInterfaceMembers_AreAccessibleThroughInterfaceAccessor()
    {
        var accessor = TypeAccessor.Create(typeof(IDerivedContract));
        IDerivedContract model = new InterfaceModel();

        accessor[model, nameof(IBaseContract.Name)] = "interface";
        accessor[model, nameof(IDerivedContract.Number)] = 9;

        Assert.AreEqual("interface", accessor[model, nameof(IBaseContract.Name)]);
        Assert.AreEqual(9, accessor[model, nameof(IDerivedContract.Number)]);
    }

    [TestMethod]
    public void AllowNonPublicAccessors_ControlsPrivateSetterButDoesNotAddPrivateMembers()
    {
        var publicAccessor = TypeAccessor.Create(typeof(MemberModel));
        var nonPublicAccessor = TypeAccessor.Create(typeof(MemberModel), allowNonPublicAccessors: true);
        var model = new MemberModel();

        Assert.IsFalse(publicAccessor.TrySetValue(model, nameof(MemberModel.IndexedValue), "public"));
        Assert.IsTrue(nonPublicAccessor.TrySetValue(model, nameof(MemberModel.IndexedValue), "non-public"));
        Assert.AreEqual("non-public", model.IndexedValue);

        Assert.IsFalse(nonPublicAccessor.TryGetValue(model, "PrivateProperty", out _));
        Assert.IsFalse(nonPublicAccessor.TryGetValue(model, "PrivateField", out _));
    }

    [TestMethod]
    public void StaticMembers_AreNotExposedByTypeAccessor()
    {
        var accessor = TypeAccessor.Create(typeof(MemberModel));
        var model = new MemberModel();

        Assert.IsFalse(accessor.TryGetValue(model, nameof(MemberModel.StaticProperty), out _));
        Assert.IsFalse(accessor.TrySetValue(model, nameof(MemberModel.StaticField), 12));
    }

    [TestMethod]
    public void TypeHelpers_BindingFlagsExposeRequestedNonPublicAndStaticMembers()
    {
        var staticMembers = TypeHelpers.GetMembers(
            typeof(MemberModel),
            BindingFlags.Public | BindingFlags.Static
        );
        var nonPublicMembers = TypeHelpers.GetMembers(
            typeof(MemberModel),
            BindingFlags.NonPublic | BindingFlags.Instance
        );

        CollectionAssert.AreEquivalent(
            new[] { nameof(MemberModel.StaticField), nameof(MemberModel.StaticProperty) },
            staticMembers.Select(member => member.Name).ToArray()
        );
        Assert.IsTrue(nonPublicMembers.Any(member => member.Name == "PrivateProperty"));
        Assert.IsTrue(nonPublicMembers.Any(member => member.Name == "PrivateField"));
    }

    [TestMethod]
    public void IndexerProperty_IsExcludedFromAccessAndMemberMetadata()
    {
        var accessor = TypeAccessor.Create(typeof(MemberModel));
        var model = new MemberModel();

        Assert.IsFalse(accessor.TryGetValue(model, "Item", out _));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => _ = accessor[model, "Item"]);
        Assert.IsFalse(accessor.GetMembers().Any(member => member.Name == "Item"));
        Assert.IsFalse(accessor.GetMemberDictionary().ContainsKey("Item"));
    }

    [TestMethod]
    public void RefReturnProperty_CanBeReadAndUpdatedThroughBothApis()
    {
        var accessor = TypeAccessor.Create(typeof(RefReturnModel));
        var model = new RefReturnModel();

        accessor[model, nameof(RefReturnModel.Value)] = 12;
        Assert.AreEqual(12, accessor[model, nameof(RefReturnModel.Value)]);

        Assert.IsTrue(accessor.TrySetValue(model, nameof(RefReturnModel.Value), 34));
        Assert.IsTrue(accessor.TryGetValue(model, nameof(RefReturnModel.Value), out var value));
        Assert.AreEqual(34, value);
    }

    [TestMethod]
    public void GetMembers_ReturnsSortedPublicInstanceMetadataAndStableDictionary()
    {
        var accessor = TypeAccessor.Create(typeof(MemberModel));
        var members = accessor.GetMembers();
        var dictionary = accessor.GetMemberDictionary();
        var expectedNames = new[]
        {
            nameof(MemberModel.IndexedValue),
            nameof(MemberModel.Name),
            nameof(MemberModel.Number),
            nameof(MemberModel.ReadOnlyField),
            nameof(MemberModel.ReadOnlyProperty),
            nameof(MemberModel.WriteOnlyProperty),
        };

        Assert.IsTrue(accessor.GetMembersSupported);
        CollectionAssert.AreEqual(expectedNames, members.Select(member => member.Name).ToArray());
        CollectionAssert.AreEquivalent(expectedNames, dictionary.Keys.ToArray());
        Assert.AreSame(
            members.Single(member => member.Name == nameof(MemberModel.Name)),
            dictionary[nameof(MemberModel.Name)]
        );
        Assert.AreEqual(typeof(string), dictionary[nameof(MemberModel.Name)].Type);
        Assert.IsFalse(dictionary.ContainsKey(nameof(MemberModel.StaticProperty)));
        Assert.IsFalse(dictionary.ContainsKey("PrivateProperty"));
    }

    [TestMethod]
    public void GetMemberDictionary_CanBeTheFirstMetadataOperation()
    {
        var accessor = TypeAccessor.Create(typeof(DictionaryFirstModel));

        var first = accessor.GetMemberDictionary();
        var second = accessor.GetMemberDictionary();

        Assert.AreSame(first, second);
        Assert.AreEqual(typeof(int), first[nameof(DictionaryFirstModel.Value)].Type);
        Assert.HasCount(1, accessor.GetMembers());
    }

    [TestMethod]
    public void Create_CachesByTypeAndNonPublicMode()
    {
        var publicAccessor = TypeAccessor.Create(typeof(MemberModel));
        var nonPublicAccessor = TypeAccessor.Create(typeof(MemberModel), true);

        Assert.AreSame(publicAccessor, TypeAccessor.Create(typeof(MemberModel)));
        Assert.AreSame(nonPublicAccessor, TypeAccessor.Create(typeof(MemberModel), true));
        Assert.AreNotSame(publicAccessor, nonPublicAccessor);
    }

    [TestMethod]
    public void BoxedStruct_PropertiesAndFieldsCanBeReadAndMutated()
    {
        object model = new StructModel { Property = 1, Field = "before" };
        var accessor = TypeAccessor.Create(typeof(StructModel));

        Assert.AreEqual(1, accessor[model, nameof(StructModel.Property)]);
        Assert.AreEqual("before", accessor[model, nameof(StructModel.Field)]);

        accessor[model, nameof(StructModel.Property)] = 2;
        accessor[model, nameof(StructModel.Field)] = "after";

        Assert.AreEqual(2, ((StructModel)model).Property);
        Assert.AreEqual("after", ((StructModel)model).Field);
    }

    [TestMethod]
    public void AnonymousAndNonPublicTypes_ExposeTheirPublicMembers()
    {
        var anonymous = new { Number = 42, Text = "value" };
        var anonymousAccessor = ObjectAccessor.Create(anonymous);
        var privateAccessor = TypeAccessor.Create(typeof(PrivateModel));

        Assert.AreEqual(42, anonymousAccessor["Number"]);
        Assert.AreEqual("value", anonymousAccessor["Text"]);
        var anonymousTypeAccessor = TypeAccessor.Create(anonymous.GetType());
        Assert.IsFalse(anonymousTypeAccessor.CreateNewSupported);
        Assert.ThrowsExactly<NotSupportedException>(() => anonymousTypeAccessor.CreateNew());

        Assert.IsTrue(privateAccessor.CreateNewSupported);
        var privateModel = privateAccessor.CreateNew();
        privateAccessor[privateModel, nameof(PrivateModel.Value)] = 7;
        Assert.AreEqual(7, privateAccessor[privateModel, nameof(PrivateModel.Value)]);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            _ = privateAccessor[privateModel, "Missing"]
        );
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            privateAccessor[privateModel, "Missing"] = 1
        );
    }

    [TestMethod]
    public void DynamicTypeAccessor_CoversIndexerTryAndUnsupportedMetadataApis()
    {
        var model = DynamicModelFactory.Create("Value", 42);
        var accessor = TypeAccessor.Create(model.GetType());

        Assert.AreEqual(42, accessor[model, "Value"]);
        accessor[model, "Value"] = 84;
        Assert.AreEqual(84, ((IDictionary<string, object?>)model)["Value"]);

        Assert.IsTrue(accessor.TryGetValue(model, "Value", out var value));
        Assert.AreEqual(84, value);
        Assert.IsFalse(accessor.TryGetValue(model, "Missing", out var missing));
        Assert.IsNull(missing);
        Assert.IsTrue(accessor.TrySetValue(model, "Created", "new"));
        Assert.AreEqual("new", ((IDictionary<string, object?>)model)["Created"]);

        var readOnly = new ReadOnlyDynamicModel();
        var readOnlyAccessor = TypeAccessor.Create(readOnly.GetType());
        Assert.IsFalse(readOnlyAccessor.TrySetValue(readOnly, "Value", 7));
        Assert.ThrowsExactly<RuntimeBinderException>(() => readOnlyAccessor[readOnly, "Value"] = 7);

        Assert.IsFalse(accessor.CreateNewSupported);
        Assert.IsFalse(accessor.GetMembersSupported);
        Assert.ThrowsExactly<NotSupportedException>(() => accessor.CreateNew());
        Assert.ThrowsExactly<NotSupportedException>(() => accessor.GetMembers());
        Assert.ThrowsExactly<NotSupportedException>(() => accessor.GetMemberDictionary());
    }

    [TestMethod]
    public void RefReadonlyProperty_CanBeReadButNotWritten()
    {
        var model = new RefReturnModel();
        var accessor = TypeAccessor.Create(typeof(RefReturnModel));
        accessor[model, nameof(RefReturnModel.Value)] = 42;

        Assert.AreEqual(42, accessor[model, nameof(RefReturnModel.ReadOnlyValue)]);
        Assert.IsFalse(accessor.TrySetValue(model, nameof(RefReturnModel.ReadOnlyValue), 7));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            accessor[model, nameof(RefReturnModel.ReadOnlyValue)] = 7
        );
        Assert.AreEqual(42, accessor[model, nameof(RefReturnModel.Value)]);
    }

    private sealed class PrivateModel
    {
        public int Value { get; set; }
    }
}
