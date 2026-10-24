using System.Reflection;

namespace HKW.FastMember.Abstractions.Tests;

[TestClass]
public sealed class AccessorContractTests
{
    [TestMethod]
    public void FrozenConstructorsAndCoreSignatures()
    {
        var constructor = typeof(TypeAccessor).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic,
            null, [typeof(Type), typeof(IEnumerable<Member>), typeof(bool), typeof(int), typeof(bool)], null)!;
        Assert.IsTrue(constructor.IsFamily);
        var parameters = constructor.GetParameters();
        CollectionAssert.AreEqual(new[] { "targetType", "members", "createNewSupported", "contractVersion", "getMembersSupported" }, parameters.Select(p => p.Name).ToArray());
        Assert.AreEqual(1, parameters[3].DefaultValue);
        Assert.AreEqual(true, parameters[4].DefaultValue);
        var genericConstructor = typeof(TypeAccessor<Model>).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic,
            null, [typeof(IEnumerable<Member>), typeof(bool), typeof(int)], null)!;
        Assert.IsTrue(genericConstructor.IsFamily);
        Assert.AreEqual(1, genericConstructor.GetParameters()[2].DefaultValue);
        foreach (var (name, arguments, result) in new[]
        {
            ("GetValueTypedCore", new[] { typeof(Model), typeof(int) }, typeof(object)),
            ("SetValueTypedCore", new[] { typeof(Model), typeof(int), typeof(object) }, typeof(void)),
            ("CreateNewTypedCore", Type.EmptyTypes, typeof(Model))
        })
        {
            var method = typeof(TypeAccessor<Model>).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic, null, arguments, null)!;
            Assert.IsTrue(method.IsFamily && method.IsVirtual);
            Assert.AreEqual(result, method.ReturnType);
        }
        Assert.IsTrue(typeof(TypeAccessor).IsAssignableFrom(typeof(TypeAccessor<Model>)));
    }

    [TestMethod]
    public void GenericAndObjectEntriesShareIndexedCores()
    {
        var accessor = new ModelAccessor();
        TypeAccessor untyped = accessor;
        var source = new Model { Value = 3 };
        Assert.AreEqual(3, accessor[source, "Value"]);
        Assert.AreEqual(1, accessor.Reads);
        Assert.AreEqual(3, untyped[source, "Value"]);
        Assert.AreEqual(2, accessor.Reads);
        accessor[source, "Value"] = 4;
        untyped[source, "Value"] = 5;
        Assert.IsTrue(accessor.TryGetValue(source, "Value", out var typed));
        Assert.IsTrue(untyped.TryGetValue(source, "Value", out var boxed));
        Assert.AreEqual(typed, boxed);
        Assert.IsTrue(accessor.TrySetValue(source, "Value", 6));
        Assert.IsTrue(untyped.TrySetValue(source, "Value", 7));
        Assert.AreEqual(4, accessor.Reads);
        Assert.AreEqual(4, accessor.Writes);
        Assert.AreEqual(7, source.Value);
        Assert.AreEqual(2, accessor.LastIndex);
        Assert.AreEqual(typeof(Model), accessor.TargetType);
        Assert.AreSame(accessor.GetMembers(), untyped.GetMembers());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void MissingAndForbiddenMembersDoNotInvokeCores(bool typed)
    {
        var accessor = new ModelAccessor();
        var model = new Model();
        Assert.IsFalse(typed ? accessor.TryGetValue(model, "WriteOnly", out _) : ((TypeAccessor)accessor).TryGetValue(model, "WriteOnly", out _));
        Assert.IsFalse(accessor.TryGetValue(model, "missing", out var missing));
        Assert.IsNull(missing);
        Assert.IsFalse(accessor.TrySetValue(model, "ReadOnly", 1));
        Assert.IsFalse(accessor.TrySetValue(model, "missing", 1));
        TypeAccessor view = accessor;
        foreach (var name in new[] { "missing", "WriteOnly" })
            Assert.AreEqual("name", Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => _ = typed ? accessor[model, name] : view[model, name]).ParamName);
        foreach (var name in new[] { "missing", "ReadOnly" })
            Assert.AreEqual("name", Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => { if (typed) accessor[model, name] = 1; else view[model, name] = 1; }).ParamName);
        Assert.AreEqual(0, accessor.Reads + accessor.Writes);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void NullInputsAlwaysThrow(bool typed)
    {
        var accessor = new ModelAccessor();
        TypeAccessor view = accessor;
        var model = new Model();
        Assert.AreEqual("target", Assert.ThrowsExactly<ArgumentNullException>(() => _ = typed ? accessor[null!, "Value"] : view[null!, "Value"]).ParamName);
        Assert.AreEqual("name", Assert.ThrowsExactly<ArgumentNullException>(() => _ = typed ? accessor[model, null!] : view[model, null!]).ParamName);
        Assert.ThrowsExactly<ArgumentNullException>(() => { if (typed) accessor[null!, "Value"] = 1; else view[null!, "Value"] = 1; });
        Assert.ThrowsExactly<ArgumentNullException>(() => { if (typed) accessor[model, null!] = 1; else view[model, null!] = 1; });
        Assert.ThrowsExactly<ArgumentNullException>(() => { if (typed) accessor.TryGetValue(null!, "Value", out _); else view.TryGetValue(null!, "Value", out _); });
        Assert.ThrowsExactly<ArgumentNullException>(() => { if (typed) accessor.TryGetValue(model, null!, out _); else view.TryGetValue(model, null!, out _); });
        Assert.ThrowsExactly<ArgumentNullException>(() => { if (typed) accessor.TrySetValue(null!, "Value", 1); else view.TrySetValue(null!, "Value", 1); });
        Assert.ThrowsExactly<ArgumentNullException>(() => { if (typed) accessor.TrySetValue(model, null!, 1); else view.TrySetValue(model, null!, 1); });
        Assert.AreEqual(0, accessor.Reads + accessor.Writes);
    }

    [TestMethod]
    public void NamesAreOrdinalCaseSensitiveAndNotTrimmed()
    {
        var accessor = new ModelAccessor();
        foreach (var name in new[] { "value", "VALUE", " Value", "Value ", "" })
            Assert.IsFalse(accessor.TryGetValue(new Model(), name, out _));
    }

    [TestMethod]
    public void GetterSetterAndConversionExceptionsPropagateUnchanged()
    {
        var accessor = new ModelAccessor();
        var model = new Model();
        Assert.ThrowsExactly<InvalidCastException>(() => accessor.TrySetValue(model, "Value", "wrong"));
        Assert.ThrowsExactly<NullReferenceException>(() => accessor.TrySetValue(model, "Value", null));
        model.Failure = new InvalidOperationException("user failure");
        Assert.AreSame(model.Failure, Assert.ThrowsExactly<InvalidOperationException>(() => accessor.TryGetValue(model, "Value", out _)));
        Assert.AreSame(model.Failure, Assert.ThrowsExactly<InvalidOperationException>(() => accessor.TrySetValue(model, "Value", 1)));
        Assert.AreSame(model.Failure, Assert.ThrowsExactly<InvalidOperationException>(() => _ = accessor[model, "Value"]));
        Assert.AreSame(model.Failure, Assert.ThrowsExactly<InvalidOperationException>(() => accessor[model, "Value"] = 1));
    }

    [TestMethod]
    public void ConstructionUsesTypedCoreFromBothViews()
    {
        var accessor = new ModelAccessor();
        Assert.IsTrue(accessor.CreateNewSupported);
        Assert.IsInstanceOfType<Model>(accessor.CreateNew());
        Assert.IsInstanceOfType<Model>(((TypeAccessor)accessor).CreateNew());
        Assert.AreEqual(2, accessor.Constructions);
        var unsupported = new ModelAccessor(false);
        Assert.ThrowsExactly<NotSupportedException>(() => unsupported.CreateNew());
        Assert.ThrowsExactly<NotSupportedException>(() => ((TypeAccessor)unsupported).CreateNew());
        Assert.AreEqual(0, unsupported.Constructions);
    }

    [TestMethod]
    public void ObjectViewRejectsIncompatibleTargetWithCastException()
    {
        TypeAccessor accessor = new ModelAccessor();
        Assert.ThrowsExactly<InvalidCastException>(() => _ = accessor[new object(), "Value"]);
        Assert.ThrowsExactly<InvalidCastException>(() => accessor[new object(), "Value"] = 1);
    }

    [TestMethod]
    public void GenericContractDoesNotDiscoverDerivedMembers()
    {
        var accessor = new ModelAccessor();
        var source = new DerivedModel();
        Assert.IsFalse(accessor.TryGetValue(source, "Extra", out _));
        Assert.AreEqual(typeof(Model), accessor.TargetType);
    }

    [TestMethod]
    public void NongenericAccessorSupportsBoxedValues()
    {
        var accessor = new BoxedAccessor();
        object box = 4;
        Assert.AreEqual(4, accessor[box, "Value"]);
        Assert.IsTrue(accessor.TryGetValue(box, "Value", out var value));
        Assert.AreEqual(4, value);
        Assert.AreEqual(typeof(int), accessor.TargetType);
    }

    [TestMethod]
    public void DynamicExtensionUsesNamesWithoutStaticMetadata()
    {
        TypeAccessor accessor = new NamedAccessor();
        var source = new Dictionary<string, object?>();
        accessor[source, "new-name"] = 10;
        Assert.AreEqual(10, accessor[source, "new-name"]);
        Assert.IsTrue(accessor.TryGetValue(source, "new-name", out var value));
        Assert.AreEqual(10, value);
        Assert.IsFalse(accessor.TryGetValue(source, "absent", out _));
        Assert.ThrowsExactly<KeyNotFoundException>(() => _ = accessor[source, "absent"]);
        Assert.IsFalse(accessor.GetMembersSupported);
        Assert.ThrowsExactly<NotSupportedException>(() => accessor.GetMembers());
        Assert.ThrowsExactly<NotSupportedException>(() => accessor.GetMemberDictionary());
        Assert.ThrowsExactly<NotSupportedException>(() => accessor.CreateNew());
    }

    [TestMethod]
    public void OptionalIndexedCoresDefaultToUnsupported()
    {
        var accessor = new BareAccessor([Describe("Value")], true);
        Assert.ThrowsExactly<NotSupportedException>(() => _ = accessor[new Model(), "Value"]);
        Assert.ThrowsExactly<NotSupportedException>(() => accessor[new Model(), "Value"] = 1);
        Assert.ThrowsExactly<NotSupportedException>(() => accessor.CreateNew());
    }

    [TestMethod]
    public void BoundWrappersShareSourceMetadataAndOperations()
    {
        var accessor = new ModelAccessor();
        var source = new Model();
        var typed = new ObjectAccessor<Model>(source, accessor);
        ObjectAccessor common = typed;
        var untyped = new ObjectAccessor(source, accessor);
        Assert.AreSame(source, typed.Source);
        Assert.AreSame(source, common.Source);
        Assert.AreSame(source, untyped.Source);
        typed["Value"] = 4;
        Assert.AreEqual(4, common["Value"]);
        Assert.IsTrue(untyped.TrySetValue("Value", 5));
        Assert.IsTrue(typed.TryGetValue("Value", out var value));
        Assert.AreEqual(5, value);
        Assert.AreSame(accessor.GetMembers(), typed.GetMembers());
        Assert.AreSame(accessor.GetMemberDictionary(), untyped.GetMemberDictionary());
        Assert.IsFalse(typed.TryGetValue("missing", out _));
        Assert.ThrowsExactly<ArgumentNullException>(() => _ = common[null!]);
    }

    [TestMethod]
    public void BoundWrappersDelegateObjectMethodsDirectlyToSource()
    {
        var source = new Model();
        var accessor = new ModelAccessor();
        ObjectAccessor[] wrappers = [new(source, accessor), new ObjectAccessor<Model>(source, accessor)];
        foreach (var wrapper in wrappers)
        {
            Assert.IsNotNull(source);
            Assert.AreEqual(source.GetHashCode(), wrapper.GetHashCode());
            Assert.AreEqual(source.ToString(), wrapper.ToString());
            Assert.AreEqual(source.Equals(source), wrapper.Equals(source));
            Assert.AreEqual(source.Equals(wrapper), wrapper.Equals(wrapper));
            Assert.AreEqual(source.Equals(null), wrapper.Equals(null));
        }
    }

    [TestMethod]
    public void BoundWrappersRejectNullAndIncompatibleBinding()
    {
        var accessor = new ModelAccessor();
        Assert.AreEqual("source", Assert.ThrowsExactly<ArgumentNullException>(() => new ObjectAccessor(null!, accessor)).ParamName);
        Assert.AreEqual("accessor", Assert.ThrowsExactly<ArgumentNullException>(() => new ObjectAccessor(new Model(), null!)).ParamName);
        Assert.ThrowsExactly<ArgumentNullException>(() => new ObjectAccessor<Model>(null!, accessor));
        Assert.ThrowsExactly<ArgumentNullException>(() => new ObjectAccessor<Model>(new Model(), null!));
        Assert.AreEqual("source", Assert.ThrowsExactly<ArgumentException>(() => new ObjectAccessor(new object(), accessor)).ParamName);
        Assert.IsNull(typeof(ObjectAccessor).GetProperty("Source")!.SetMethod);
        Assert.IsNull(typeof(ObjectAccessor<Model>).GetProperty("Source", BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)!.SetMethod);
    }

    [TestMethod]
    public void DynamicBoundWrapperRetainsUnsupportedMetadata()
    {
        var wrapper = new ObjectAccessor(new Dictionary<string, object?>(), new NamedAccessor());
        wrapper["arbitrary"] = null;
        Assert.IsTrue(wrapper.TryGetValue("arbitrary", out var value));
        Assert.IsNull(value);
        Assert.ThrowsExactly<NotSupportedException>(() => wrapper.GetMembers());
        Assert.ThrowsExactly<NotSupportedException>(() => wrapper.GetMemberDictionary());
    }

    [TestMethod]
    public void FrozenGenericMetadataCapabilityConstructor()
    {
        var constructor = typeof(TypeAccessor<Model>).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic,
            null, [typeof(IEnumerable<Member>), typeof(bool), typeof(int), typeof(bool)], null)!;
        Assert.IsNotNull(constructor);
        Assert.IsTrue(constructor.IsFamily);
        CollectionAssert.AreEqual(new[] { "members", "createNewSupported", "contractVersion", "getMembersSupported" },
            constructor.GetParameters().Select(p => p.Name).ToArray());
        Assert.IsFalse(constructor.GetParameters().Any(p => p.IsOptional));
    }

    [TestMethod]
    public void TypedNameOnlyAccessorDispatchesEveryEntryWithoutIndexedFallback()
    {
        TypeAccessor<Dictionary<string, object?>> accessor = new TypedNamedAccessor();
        TypeAccessor untyped = accessor;
        var source = new Dictionary<string, object?>();
        accessor[source, "first"] = 10;
        Assert.AreEqual(10, accessor[source, "first"]);
        Assert.IsTrue(accessor.TrySetValue(source, "second", null));
        Assert.IsTrue(accessor.TryGetValue(source, "second", out var value));
        Assert.IsNull(value);
        untyped[source, "third"] = 30;
        Assert.AreEqual(30, untyped[source, "third"]);
        Assert.IsTrue(untyped.TrySetValue(source, "fourth", 40));
        Assert.IsTrue(untyped.TryGetValue(source, "fourth", out value));
        Assert.AreEqual(40, value);
        Assert.IsFalse(accessor.TryGetValue(source, "missing", out value));
        Assert.IsNull(value);
        Assert.ThrowsExactly<KeyNotFoundException>(() => _ = accessor[source, "missing"]);
        Assert.ThrowsExactly<KeyNotFoundException>(() => _ = untyped[source, "missing"]);
        Assert.AreEqual(typeof(Dictionary<string, object?>), accessor.TargetType);
        Assert.AreEqual(7, accessor.ContractVersion);
        Assert.IsFalse(accessor.GetMembersSupported);
        Assert.ThrowsExactly<NotSupportedException>(() => accessor.GetMembers());
        Assert.ThrowsExactly<NotSupportedException>(() => untyped.GetMemberDictionary());
        Assert.ThrowsExactly<NotSupportedException>(() => accessor.CreateNew());
    }

    [TestMethod]
    public void TypedNameOnlyAccessorValidatesNullsThroughBothViews()
    {
        TypeAccessor<Dictionary<string, object?>> accessor = new TypedNamedAccessor();
        TypeAccessor untyped = accessor;
        var source = new Dictionary<string, object?>();
        Assert.ThrowsExactly<ArgumentNullException>(() => _ = accessor[null!, "key"]);
        Assert.ThrowsExactly<ArgumentNullException>(() => _ = accessor[source, null!]);
        Assert.ThrowsExactly<ArgumentNullException>(() => accessor[null!, "key"] = 1);
        Assert.ThrowsExactly<ArgumentNullException>(() => accessor[source, null!] = 1);
        Assert.ThrowsExactly<ArgumentNullException>(() => accessor.TryGetValue(null!, "key", out _));
        Assert.ThrowsExactly<ArgumentNullException>(() => accessor.TryGetValue(source, null!, out _));
        Assert.ThrowsExactly<ArgumentNullException>(() => accessor.TrySetValue(null!, "key", 1));
        Assert.ThrowsExactly<ArgumentNullException>(() => accessor.TrySetValue(source, null!, 1));
        Assert.ThrowsExactly<ArgumentNullException>(() => _ = untyped[null!, "key"]);
        Assert.ThrowsExactly<ArgumentNullException>(() => _ = untyped[source, null!]);
        Assert.ThrowsExactly<ArgumentNullException>(() => untyped[null!, "key"] = 1);
        Assert.ThrowsExactly<ArgumentNullException>(() => untyped[source, null!] = 1);
        Assert.ThrowsExactly<ArgumentNullException>(() => untyped.TryGetValue(null!, "key", out _));
        Assert.ThrowsExactly<ArgumentNullException>(() => untyped.TryGetValue(source, null!, out _));
        Assert.ThrowsExactly<ArgumentNullException>(() => untyped.TrySetValue(null!, "key", 1));
        Assert.ThrowsExactly<ArgumentNullException>(() => untyped.TrySetValue(source, null!, 1));
    }

    [TestMethod]
    public void TypedAndUntypedBoundWrappersRetainNameOnlyBehavior()
    {
        var source = new Dictionary<string, object?>();
        var accessor = new TypedNamedAccessor();
        var typed = new ObjectAccessor<Dictionary<string, object?>>(source, accessor);
        ObjectAccessor[] wrappers = [typed, new(source, accessor)];
        Assert.AreSame(source, typed.Source);
        foreach (var wrapper in wrappers)
        {
            Assert.AreSame(source, wrapper.Source);
            wrapper["key"] = 10;
            Assert.AreEqual(10, wrapper["key"]);
            Assert.IsTrue(wrapper.TrySetValue("key", null));
            Assert.IsTrue(wrapper.TryGetValue("key", out var value));
            Assert.IsNull(value);
            Assert.IsFalse(wrapper.TryGetValue("missing", out _));
            Assert.ThrowsExactly<KeyNotFoundException>(() => _ = wrapper["missing"]);
            Assert.ThrowsExactly<NotSupportedException>(() => wrapper.GetMembers());
            Assert.ThrowsExactly<NotSupportedException>(() => wrapper.GetMemberDictionary());
            Assert.ThrowsExactly<ArgumentNullException>(() => _ = wrapper[null!]);
            Assert.ThrowsExactly<ArgumentNullException>(() => wrapper[null!] = 1);
            Assert.ThrowsExactly<ArgumentNullException>(() => wrapper.TryGetValue(null!, out _));
            Assert.ThrowsExactly<ArgumentNullException>(() => wrapper.TrySetValue(null!, 1));
        }
    }

    private sealed class TypedNamedAccessor() : TypeAccessor<Dictionary<string, object?>>([], false, 7, false)
    {
        public override object? this[object target, string name]
        {
            get { ArgumentNullException.ThrowIfNull(target); ArgumentNullException.ThrowIfNull(name); return ((Dictionary<string, object?>)target)[name]; }
            set { ArgumentNullException.ThrowIfNull(target); ArgumentNullException.ThrowIfNull(name); ((Dictionary<string, object?>)target)[name] = value; }
        }
        public override bool TryGetValue(object target, string name, out object? value)
        {
            ArgumentNullException.ThrowIfNull(target);
            ArgumentNullException.ThrowIfNull(name);
            return ((Dictionary<string, object?>)target).TryGetValue(name, out value);
        }
        public override bool TrySetValue(object target, string name, object? value) { this[target, name] = value; return true; }
        protected override object? GetValueTypedCore(Dictionary<string, object?> target, int index) => throw new AssertFailedException("Indexed access must not run.");
        protected override void SetValueTypedCore(Dictionary<string, object?> target, int index, object? value) => throw new AssertFailedException("Indexed access must not run.");
    }

    internal static Member Describe(string name, bool read = true, bool write = true) =>
        new(name, typeof(int), -1, read, write, $"Model.{name}", typeof(int), false);

    internal class Model
    {
        public int Value { get; set; }
        public Exception? Failure { get; set; }
        public override string ToString() => "model";
    }

    private sealed class DerivedModel : Model { public string Extra => "extra"; }

    internal sealed class ModelAccessor(bool createNewSupported = true) : TypeAccessor<Model>(
        [Describe("ReadOnly", write: false), Describe("Throw"), Describe("Value"), Describe("WriteOnly", read: false)], createNewSupported)
    {
        public int Reads, Writes, Constructions, LastIndex;
        protected override object? GetValueTypedCore(Model target, int index)
        {
            Reads++;
            LastIndex = index;
            if (target.Failure is not null) throw target.Failure;
            return target.Value;
        }
        protected override void SetValueTypedCore(Model target, int index, object? value)
        {
            Writes++;
            LastIndex = index;
            if (target.Failure is not null) throw target.Failure;
            target.Value = (int)value!;
        }
        protected override Model CreateNewTypedCore() { Constructions++; return new(); }
    }

    internal sealed class BareAccessor(IEnumerable<Member> members, bool create = false, int version = 1, bool metadata = true)
        : TypeAccessor(typeof(Model), members, create, version, metadata);

    private sealed class BoxedAccessor() : TypeAccessor(typeof(int), [Describe("Value", write: false)], false)
    {
        protected override object? GetValueCore(object target, int index) => (int)target;
    }

    private sealed class NamedAccessor() : TypeAccessor(typeof(Dictionary<string, object?>), [], false, getMembersSupported: false)
    {
        public override object? this[object target, string name]
        {
            get { ArgumentNullException.ThrowIfNull(target); ArgumentNullException.ThrowIfNull(name); return ((Dictionary<string, object?>)target)[name]; }
            set { ArgumentNullException.ThrowIfNull(target); ArgumentNullException.ThrowIfNull(name); ((Dictionary<string, object?>)target)[name] = value; }
        }
        public override bool TryGetValue(object target, string name, out object? value)
        {
            ArgumentNullException.ThrowIfNull(target);
            ArgumentNullException.ThrowIfNull(name);
            return ((Dictionary<string, object?>)target).TryGetValue(name, out value);
        }
        public override bool TrySetValue(object target, string name, object? value) { this[target, name] = value; return true; }
    }
}
