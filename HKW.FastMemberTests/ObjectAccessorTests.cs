using System.Dynamic;
using HKW.FastMember;
using Microsoft.CSharp.RuntimeBinder;

namespace HKW.FastMemberTests;

[TestClass]
public class ObjectAccessorTests
{
    [TestMethod]
    public void ProtectedConstructor_ValidatesSourceForDerivedAccessors()
    {
        var error = Assert.ThrowsExactly<ArgumentNullException>(() =>
            new ConstructorProbeAccessor(null!)
        );

        Assert.AreEqual("source", error.ParamName);
    }

    [TestMethod]
    public void Create_OrdinaryObject_GetsAndSetsPropertiesAndFields()
    {
        var model = new MemberModel { Name = "before", Number = 1 };
        var accessor = ObjectAccessor.Create(model);

        Assert.AreSame(model, accessor.Source);
        Assert.AreEqual("before", accessor["Name"]);
        Assert.AreEqual(1, accessor["Number"]);

        accessor["Name"] = "after";
        accessor["Number"] = 2;

        Assert.AreEqual("after", model.Name);
        Assert.AreEqual(2, model.Number);
    }

    [TestMethod]
    public void Create_WithNonPublicAccessors_UsesRequestedAccessorSet()
    {
        var model = new MemberModel();
        var publicAccessor = ObjectAccessor.Create(model);
        var nonPublicAccessor = ObjectAccessor.Create(model, allowNonPublicAccessors: true);

        Assert.IsFalse(publicAccessor.TrySetValue("IndexedValue", "hidden"));
        Assert.IsTrue(nonPublicAccessor.TrySetValue("IndexedValue", "hidden"));

        Assert.AreEqual("hidden", model.IndexedValue);
        Assert.IsFalse(nonPublicAccessor.TrySetValue("PrivateProperty", "not exposed"));
        Assert.IsFalse(nonPublicAccessor.TrySetValue("PrivateField", 17));
    }

    [TestMethod]
    public void Create_DynamicObject_GetsAndSetsMembers()
    {
        ExpandoObject model = DynamicModelFactory.Create("Value", 42);
        var accessor = ObjectAccessor.Create(model);

        Assert.AreSame(model, accessor.Source);
        Assert.AreEqual(42, accessor["Value"]);

        accessor["Value"] = 84;

        Assert.AreEqual(84, ((IDictionary<string, object?>)model)["Value"]);
    }

    [TestMethod]
    public void TryGetValueAndTrySetValue_OrdinaryMembers_ReportSuccessAndMissingMembers()
    {
        var model = new MemberModel { Name = "before" };
        var accessor = ObjectAccessor.Create(model);

        Assert.IsTrue(accessor.TryGetValue("Name", out var value));
        Assert.AreEqual("before", value);
        Assert.IsTrue(accessor.TrySetValue("Name", "after"));
        Assert.AreEqual("after", model.Name);

        Assert.IsFalse(accessor.TryGetValue("Missing", out var missingValue));
        Assert.IsNull(missingValue);
        Assert.IsFalse(accessor.TrySetValue("Missing", "value"));
    }

    [TestMethod]
    public void TryGetValue_DynamicMissingMember_ReturnsFalse()
    {
        var accessor = ObjectAccessor.Create(DynamicModelFactory.Create());

        Assert.IsFalse(accessor.TryGetValue("Missing", out var value));
        Assert.IsNull(value);
        Assert.ThrowsExactly<RuntimeBinderException>(() => _ = accessor["Missing"]);
    }

    [TestMethod]
    public void ExpandoObject_SettingMissingMembers_CreatesThem()
    {
        ExpandoObject model = DynamicModelFactory.Create();
        var values = (IDictionary<string, object?>)model;
        var accessor = ObjectAccessor.Create(model);

        accessor["IndexerCreated"] = "first";
        Assert.AreEqual("first", values["IndexerCreated"]);

        Assert.IsTrue(accessor.TrySetValue("TryCreated", "second"));
        Assert.AreEqual("second", values["TryCreated"]);
        Assert.IsTrue(accessor.TryGetValue("TryCreated", out var value));
        Assert.AreEqual("second", value);
    }

    [TestMethod]
    public void MemberMetadata_IsForwardedForOrdinaryObjectsAndUnsupportedForDynamicObjects()
    {
        var ordinary = ObjectAccessor.Create(new MemberModel());
        var members = ordinary.GetMembers();
        var dictionary = ordinary.GetMemberDictionary();

        CollectionAssert.AreEqual(
            members.Select(member => member.Name).ToArray(),
            dictionary.Keys.OrderBy(name => name, StringComparer.Ordinal).ToArray()
        );
        Assert.AreSame(
            members.Single(member => member.Name == nameof(MemberModel.Name)),
            dictionary[nameof(MemberModel.Name)]
        );

        var dynamicAccessor = ObjectAccessor.Create(DynamicModelFactory.Create());
        Assert.ThrowsExactly<NotSupportedException>(() => dynamicAccessor.GetMembers());
        Assert.ThrowsExactly<NotSupportedException>(() => dynamicAccessor.GetMemberDictionary());
    }

    [TestMethod]
    public void DynamicTrySetValue_ReturnsFalseWhenBinderRejectsTheWrite()
    {
        var accessor = ObjectAccessor.Create(new ReadOnlyDynamicModel());

        Assert.AreEqual(42, accessor["Value"]);
        Assert.IsFalse(accessor.TrySetValue("Value", 7));
        Assert.ThrowsExactly<RuntimeBinderException>(() => accessor["Value"] = 7);
    }

    [TestMethod]
    public void Wrapper_BindsOperationsToItsSourceObject()
    {
        var first = new MemberModel { Name = "first" };
        var second = new MemberModel { Name = "second" };
        var firstAccessor = ObjectAccessor.Create(first);
        var secondAccessor = ObjectAccessor.Create(second);

        firstAccessor["Name"] = "changed";

        Assert.AreSame(first, firstAccessor.Source);
        Assert.AreSame(second, secondAccessor.Source);
        Assert.AreEqual("changed", first.Name);
        Assert.AreEqual("second", secondAccessor["Name"]);
        Assert.IsTrue(firstAccessor.Equals(first));
        Assert.AreEqual(first.GetHashCode(), firstAccessor.GetHashCode());
        Assert.AreEqual(first.ToString(), firstAccessor.ToString());
    }

    private sealed class ConstructorProbeAccessor(object source) : ObjectAccessor(source)
    {
        public override object this[string name]
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override bool TryGetValue(string name, out object value)
        {
            value = null!;
            return false;
        }

        public override bool TrySetValue(string name, object value) => false;
    }
}
