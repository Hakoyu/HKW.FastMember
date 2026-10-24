using System.Diagnostics.CodeAnalysis;
using Member = HKW.FastMember.Abstractions.Member;

namespace HKW.FastMember.Generater.Tests;

[TestClass]
public sealed class AccessorTests
{
    [TestMethod]
    public void GeneratedAccessorsReadWritePropertiesAndFields()
    {
        var source = new AccessorModel();
        var accessor = TypeAccessor.Create<AccessorModel>();
        accessor[source, "Number"] = 42;
        accessor[source, "Name"] = "value";
        accessor[source, "Optional"] = null;
        Assert.AreEqual(42, source.Number);
        Assert.AreEqual("value", accessor[source, "Name"]);
        Assert.IsNull(accessor[source, "Optional"]);
        Assert.IsTrue(accessor.TrySetValue(source, "Number", 7));
        Assert.IsTrue(accessor.TryGetValue(source, "Number", out var number));
        Assert.AreEqual(7, number);
        Assert.IsTrue(accessor.CreateNewSupported);
        Assert.IsInstanceOfType<AccessorModel>(accessor.CreateNew());
    }

    [TestMethod]
    public void BoundAccessorUsesStaticTypeAndRetainsSource()
    {
        BaseModel source = new DerivedModel();
        var bound = ObjectAccessor.Create(source);
        Assert.AreSame(source, bound.Source);
        bound["Value"] = 9;
        Assert.AreEqual(9, source.Value);
        Assert.AreEqual("derived", bound["Virtual"]);
        Assert.IsFalse(bound.TryGetValue("Extra", out var missing));
        Assert.IsNull(missing);
        Assert.IsFalse(bound.TrySetValue("Extra", 1));
        Assert.ThrowsExactly<ArgumentNullException>(() => ObjectAccessor.Create<BaseModel>(null!));
    }

    [TestMethod]
    public void MetadataIsSortedAndReflectsAccessorPermissions()
    {
        var accessor = TypeAccessor.Create<AccessorModel>();
        var members = accessor.GetMembers();
        var names = members.Select(member => member.Name).ToArray();
        CollectionAssert.AreEqual(
            names.OrderBy(name => name, StringComparer.Ordinal).ToArray(),
            names
        );
        Assert.IsFalse(names.Contains("Static"));
        Assert.IsFalse(names.Contains("Constant"));
        Assert.IsFalse(names.Contains("Item"));
        var dictionary = accessor.GetMemberDictionary();
        Assert.AreEqual(typeof(int), dictionary["Number"].Type);
        Assert.AreEqual(2, dictionary["Number"].Ordinal);
        Assert.IsTrue(dictionary["Number"].CanRead);
        Assert.IsTrue(dictionary["Number"].CanWrite);
        Assert.IsFalse(dictionary["ReadOnly"].CanWrite);
        Assert.IsFalse(dictionary["InitOnly"].CanWrite);
        Assert.IsFalse(dictionary["PrivateSetter"].CanWrite);
        Assert.IsFalse(dictionary["WriteOnly"].CanRead);
        Assert.IsTrue(dictionary["WriteOnly"].CanWrite);
        Assert.AreEqual(-1, dictionary["Name"].Ordinal);
        if (members is IList<Member> list)
            Assert.ThrowsExactly<NotSupportedException>(() => list.Clear());
        if (dictionary is IDictionary<string, Member> mutable)
            Assert.ThrowsExactly<NotSupportedException>(() => mutable.Clear());
        Assert.AreEqual(names.Length, accessor.GetMembers().Count);
    }

    [TestMethod]
    public void UnknownAndUnavailableMembersHaveConsistentExceptions()
    {
        var accessor = TypeAccessor.Create<AccessorModel>();
        var source = new AccessorModel();
        foreach (var name in new[] { "Missing", "number", "Static" })
        {
            Assert.AreEqual(
                "name",
                Assert
                    .ThrowsExactly<ArgumentOutOfRangeException>(() => _ = accessor[source, name])
                    .ParamName
            );
            Assert.IsFalse(accessor.TryGetValue(source, name, out var value));
            Assert.IsNull(value);
            Assert.IsFalse(accessor.TrySetValue(source, name, 1));
        }
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => accessor[source, "ReadOnly"] = 2);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => _ = accessor[source, "WriteOnly"]);
        Assert.IsFalse(accessor.TrySetValue(source, "InitOnly", 1));
        Assert.IsFalse(accessor.TrySetValue(source, "PrivateSetter", 1));
        Assert.IsFalse(accessor.TryGetValue(source, "WriteOnly", out var unreadable));
        Assert.IsNull(unreadable);
        Assert.IsTrue(accessor.TrySetValue(source, "WriteOnly", 16));
        Assert.AreEqual(16, source.Written);
    }

    [TestMethod]
    public void NullsAndUserExceptionsAreNotSwallowed()
    {
        var accessor = TypeAccessor.Create<AccessorModel>();
        var source = new AccessorModel();
        Assert.ThrowsExactly<ArgumentNullException>(() => _ = accessor[null!, "Name"]);
        Assert.ThrowsExactly<ArgumentNullException>(() => accessor.TrySetValue(null!, "Name", "a"));
        Assert.ThrowsExactly<ArgumentNullException>(() =>
            accessor.TryGetValue(source, null!, out _)
        );
        Assert.ThrowsExactly<ArgumentNullException>(() =>
            accessor.TrySetValue(source, null!, null)
        );
        Assert.ThrowsExactly<ArgumentNullException>(() => _ = accessor[source, null!]);
        Assert.ThrowsExactly<InvalidCastException>(() =>
            accessor.TrySetValue(source, "Number", "bad")
        );
        Assert.ThrowsExactly<NullReferenceException>(() =>
            accessor.TrySetValue(source, "Number", null)
        );
        var exceptionAccessor = TypeAccessor.Create<ThrowingModel>();
        var throwing = new ThrowingModel();
        Assert.AreSame(
            throwing.Failure,
            Assert.ThrowsExactly<InvalidOperationException>(() =>
                exceptionAccessor.TryGetValue(throwing, "Value", out _)
            )
        );
        Assert.AreSame(
            throwing.Failure,
            Assert.ThrowsExactly<InvalidOperationException>(() =>
                exceptionAccessor.TrySetValue(throwing, "Value", 1)
            )
        );
    }

    [TestMethod]
    public void HiddenNonPublicAndStaticMembersDoNotHideEligibleBaseMembers()
    {
        var accessor = TypeAccessor.Create<HiddenModel>();
        var source = new HiddenModel();
        accessor[source, "Value"] = 12;
        accessor[source, "Text"] = "base text";
        Assert.AreEqual(12, ((BaseModel)source).Value);
        Assert.AreEqual("base text", ((BaseModel)source).Text);
        Assert.AreEqual(23, HiddenModel.Value);
        Assert.AreEqual("derived", accessor[source, "Virtual"]);
    }

    [TestMethod]
    public void PublicDerivedMembersTakePrecedence()
    {
        var accessor = TypeAccessor.Create<PublicHiddenModel>();
        var source = new PublicHiddenModel();
        accessor[source, "Value"] = "derived value";
        Assert.AreEqual("derived value", source.Value);
        Assert.AreEqual(0, ((BaseModel)source).Value);
        Assert.AreEqual(typeof(string), accessor.GetMemberDictionary()["Value"].Type);
    }

    [TestMethod]
    public void ClosedGenericsRecordsAndNestedKeywordNamesAreSupported()
    {
        var generic = TypeAccessor.Create<GenericModel<string>>();
        var source = new GenericModel<string>();
        generic[source, "Value"] = "generic";
        Assert.AreEqual("generic", source.Value);
        var record = TypeAccessor.Create<RecordModel>();
        Assert.IsFalse(record.GetMemberDictionary()["Id"].CanWrite);
        Assert.IsFalse(record.CreateNewSupported);
        var nested = TypeAccessor.Create<Container.Nested>();
        var item = nested.CreateNew();
        nested[item, "event"] = "keyword";
        Assert.AreEqual("keyword", item.@event);
    }

    [TestMethod]
    public void ConstructionRespectsPublicAndRequiredContracts()
    {
        Assert.IsFalse(TypeAccessor.Create<AbstractModel>().CreateNewSupported);
        Assert.IsFalse(TypeAccessor.Create<InternalConstructor>().CreateNewSupported);
        Assert.IsFalse(TypeAccessor.Create<RequiredModel>().CreateNewSupported);
        Assert.IsFalse(TypeAccessor.Create<PrivateConstructor>().CreateNewSupported);
        Assert.IsTrue(TypeAccessor.Create<SatisfiedRequiredModel>().CreateNewSupported);
        Assert.AreEqual("ready", TypeAccessor.Create<SatisfiedRequiredModel>().CreateNew().Name);
        Assert.ThrowsExactly<NotSupportedException>(() =>
            TypeAccessor.Create<InternalConstructor>().CreateNew()
        );
        Assert.ThrowsExactly<NotSupportedException>(() =>
            TypeAccessor.Create<RequiredModel>().CreateNew()
        );
        Assert.ThrowsExactly<ApplicationException>(() =>
            TypeAccessor.Create<ThrowingConstructor>().CreateNew()
        );
    }

    public class AccessorModel
    {
        [global::HKW.FastMember.Abstractions.Ordinal(2)]
        public int Number;
        public string? Name { get; set; }
        public int? Optional { get; set; }
        public readonly int ReadOnly = 10;
        public int InitOnly { get; init; }
        public int PrivateSetter { get; private set; }
        public int WriteOnly { private get; set; }
        public int Written => WriteOnly;
        public static int Static { get; set; }
        public const int Constant = 5;
        public int this[int index] => index;
    }

    public class BaseModel
    {
        public int Value { get; set; }
        public string? Text { get; set; }
        public virtual string Virtual => "base";
    }

    public sealed class DerivedModel : BaseModel
    {
        public int Extra { get; set; }
        public override string Virtual => "derived";
    }

    public sealed class HiddenModel : BaseModel
    {
        public static new int Value => 23;
        private new string Text => "private";
        public override string Virtual => "derived";
    }

    public sealed class PublicHiddenModel : BaseModel
    {
        public new string? Value { get; set; }
    }

    public sealed class ThrowingModel
    {
        public InvalidOperationException Failure { get; } = new("from property");
        public int Value
        {
            get => throw Failure;
            set => throw Failure;
        }
    }

    public sealed class GenericModel<T>
    {
        public T? Value { get; set; }
    }

    public sealed record RecordModel(int Id);

    public static class Container
    {
        public sealed class Nested
        {
            public string? @event { get; set; }
        }
    }

    public abstract class AbstractModel
    {
        public int Value { get; set; }
    }

    public sealed class InternalConstructor
    {
        internal InternalConstructor() { }
    }

    public sealed class PrivateConstructor
    {
        private PrivateConstructor() { }
    }

    public sealed class RequiredModel
    {
        public required string Name { get; init; }
    }

    public sealed class SatisfiedRequiredModel
    {
        public required string Name { get; init; }

        [SetsRequiredMembers]
        public SatisfiedRequiredModel() => Name = "ready";
    }

    public sealed class ThrowingConstructor
    {
        public ThrowingConstructor() => throw new ApplicationException("constructor");
    }
}
