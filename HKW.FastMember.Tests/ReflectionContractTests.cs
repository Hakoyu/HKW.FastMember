using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using HKW.FastMember;
using Common = HKW.FastMember.Abstractions;

namespace HKW.FastMember.Tests;

[TestClass]
public sealed class ReflectionContractTests
{
    [TestMethod]
    public void PublicAndRestrictedModels_ExerciseBothEmitPathsWithSharedPermissions()
    {
        var publicAccessor = TypeAccessor.Create(typeof(PermissionModel));
        var restrictedAccessor = TypeAccessor.Create(typeof(PermissionModel), true);
        Assert.IsTrue(publicAccessor.GetType().Assembly.IsDynamic);
        Assert.IsFalse(restrictedAccessor.GetType().Assembly.IsDynamic);
        foreach (var accessor in new[] { publicAccessor, restrictedAccessor })
        {
            var model = new PermissionModel();
            Assert.IsTrue(accessor.TrySetValue(model, nameof(PermissionModel.Value), 12));
            Assert.AreEqual(12, accessor[model, nameof(PermissionModel.Value)]);
            foreach (
                var name in new[]
                {
                    nameof(PermissionModel.ReadOnlyField),
                    nameof(PermissionModel.InitOnly),
                    nameof(PermissionModel.GetterOnly),
                }
            )
            {
                Assert.IsFalse(accessor.GetMemberDictionary()[name].CanWrite, name);
                Assert.IsFalse(accessor.TrySetValue(model, name, 7), name);
                Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => accessor[model, name] = 7);
            }
            Assert.IsFalse(accessor.TryGetValue(model, nameof(PermissionModel.Span), out _));
            Assert.IsFalse(accessor.GetMemberDictionary()[nameof(PermissionModel.Span)].CanRead);
        }
        Assert.IsFalse(
            publicAccessor.GetMemberDictionary()[nameof(PermissionModel.PrivateSet)].CanWrite
        );
        Assert.IsTrue(
            restrictedAccessor.GetMemberDictionary()[nameof(PermissionModel.PrivateSet)].CanWrite
        );
        var writable = new PermissionModel();
        restrictedAccessor[writable, nameof(PermissionModel.PrivateSet)] = 21;
        Assert.AreEqual(21, writable.PrivateSet);
        Assert.AreEqual(1, publicAccessor.ContractVersion);
        Assert.AreEqual(typeof(PermissionModel), publicAccessor.TargetType);
    }

    [TestMethod]
    public void RequiredMembers_RequireSetsRequiredMembersIncludingInheritedRequirements()
    {
        foreach (var type in new[] { typeof(RequiredModel), typeof(InheritedRequiredModel) })
        {
            foreach (var allowNonPublic in new[] { false, true })
            {
                var accessor = TypeAccessor.Create(type, allowNonPublic);
                Assert.IsFalse(accessor.CreateNewSupported);
                Assert.ThrowsExactly<NotSupportedException>(() => accessor.CreateNew());
            }
        }
        var supported = TypeAccessor.Create<InitializedRequiredModel>();
        Assert.IsTrue(supported.CreateNewSupported);
        Assert.AreEqual("initialized", supported.CreateNew().Name);
        Assert.AreEqual(
            "initialized",
            ((InitializedRequiredModel)((Common.TypeAccessor)supported).CreateNew()).Name
        );
    }

    [TestMethod]
    public void GenericBridge_UsesStaticContractAndSharedDescriptorsAndCaches()
    {
        var typed = TypeAccessor.Create<HiddenDerivedModel>();
        Common.TypeAccessor untyped = typed;
        var runtime = TypeAccessor.Create(typeof(HiddenDerivedModel));
        var model = new HiddenDerivedModel();
        typed[model, nameof(HiddenDerivedModel.Value)] = "typed";
        Assert.AreEqual("typed", untyped[model, nameof(HiddenDerivedModel.Value)]);
        Assert.IsTrue(untyped.TrySetValue(model, nameof(HiddenDerivedModel.Value), "untyped"));
        Assert.IsTrue(typed.TryGetValue(model, nameof(HiddenDerivedModel.Value), out var value));
        Assert.AreEqual("untyped", value);
        Assert.AreSame(runtime.GetMembers()[0], typed.GetMembers()[0]);
        Assert.AreSame(typed, TypeAccessor.Create<HiddenDerivedModel>());
        Assert.AreNotSame(typed, TypeAccessor.Create<HiddenDerivedModel>(true));
        var staticBase = TypeAccessor.Create<HiddenBaseModel>();
        staticBase[model, nameof(HiddenBaseModel.Value)] = 9;
        Assert.AreEqual(9, ((HiddenBaseModel)model).Value);
        Assert.AreEqual("untyped", model.Value);
    }

    [TestMethod]
    public void ObjectFactories_DistinguishRuntimeAndExplicitStaticObjectContracts()
    {
        object model = new DerivedModel { BaseValue = "base", DerivedValue = 12 };
        var runtime = ObjectAccessor.Create(model);
        var staticObject = ObjectAccessor.Create<object>(model);
        var staticBase = ObjectAccessor.Create<BaseModel>((BaseModel)model);
        Assert.AreEqual(12, runtime[nameof(DerivedModel.DerivedValue)]);
        Assert.IsFalse(staticObject.TryGetValue(nameof(DerivedModel.DerivedValue), out _));
        Assert.IsFalse(staticBase.TryGetValue(nameof(DerivedModel.DerivedValue), out _));
        Assert.AreEqual("base", staticBase[nameof(BaseModel.BaseValue)]);
        Assert.AreSame(model, staticBase.Source);
    }

    [TestMethod]
    public void DerivedField_HidesBasePropertyAndOverridesRemainVirtual()
    {
        var accessor = TypeAccessor.Create<FieldHidingModel>();
        var model = new FieldHidingModel();
        accessor[model, "Name"] = 42;
        Assert.AreEqual(42, model.Name);
        Assert.IsNull(((PropertyBaseModel)model).Name);
        Assert.AreEqual(typeof(int), accessor.GetMemberDictionary()["Name"].Type);
        var virtualBase = TypeAccessor.Create<VirtualBaseModel>();
        Assert.AreEqual(20, virtualBase[new VirtualDerivedModel(), "Value"]);
    }

    [TestMethod]
    public void GenericInterface_UsesInheritedMembersWithoutRuntimeDiscovery()
    {
        var accessor = TypeAccessor.Create<IDerivedContract>();
        IDerivedContract model = new InterfaceModel();
        accessor[model, "Name"] = "interface";
        Assert.IsTrue(accessor.TrySetValue(model, "Number", 13));
        Assert.AreEqual("interface", model.Name);
        Assert.AreEqual(13, model.Number);
        Assert.IsFalse(accessor.CreateNewSupported);
    }

    [TestMethod]
    public void NonPublicNestedModel_PreservesReadOnlyPropertiesAndOriginalBoxUpdates()
    {
        object model = new RestrictedStruct();
        var accessor = TypeAccessor.Create(typeof(RestrictedStruct), true);
        Assert.IsFalse(accessor.GetType().Assembly.IsDynamic);
        accessor[model, nameof(RestrictedStruct.Value)] = 33;
        Assert.AreEqual(33, ((RestrictedStruct)model).Value);
        Assert.AreEqual(33, accessor[model, nameof(RestrictedStruct.ReadOnlyValue)]);
        Assert.IsFalse(
            accessor.GetMemberDictionary()[nameof(RestrictedStruct.ReadOnlyValue)].CanWrite
        );
        Assert.IsFalse(accessor.TrySetValue(model, nameof(RestrictedStruct.ReadOnlyValue), 2));
    }

    [TestMethod]
    public void ReflectionExtensions_RejectNonReflectionDescriptorsWithoutRediscovery()
    {
        Common.Member member = new(
            "Value",
            typeof(int),
            -1,
            true,
            false,
            "synthetic",
            typeof(int),
            false
        );
        Assert.ThrowsExactly<NotSupportedException>(() => member.AsReflectionMember());
        Assert.ThrowsExactly<NotSupportedException>(() => member.GetAttribute<ObsoleteAttribute>());
        Assert.ThrowsExactly<ArgumentNullException>(() =>
            ((Common.Member)null!).AsReflectionMember()
        );
        Common.Member reflected = TypeAccessor.Create(typeof(MemberModel)).GetMemberDictionary()[
            "Name"
        ];
        Assert.IsInstanceOfType<PropertyInfo>(reflected.AsReflectionMember().MemberInfo);
    }

    [TestMethod]
    public void RestrictedTypedBridge_PropagatesUserAndConversionErrors()
    {
        var accessor = TypeAccessor.Create<RestrictedThrowingModel>();
        Common.TypeAccessor bridge = accessor;
        var model = new RestrictedThrowingModel();
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            accessor.TryGetValue(model, "Failure", out _)
        );
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            bridge.TrySetValue(model, "Failure", "value")
        );
        Assert.ThrowsExactly<InvalidCastException>(() =>
            accessor.TrySetValue(model, "Value", "wrong")
        );
        Assert.ThrowsExactly<ArgumentNullException>(() =>
            accessor.TryGetValue(null!, "Missing", out _)
        );
        Assert.ThrowsExactly<ArgumentNullException>(() => bridge.TrySetValue(model, null!, 1));
    }

    [TestMethod]
    public void RuntimeTargetValidation_RejectsBaseObjectsEvenForInheritedMembers()
    {
        var accessor = TypeAccessor.Create(typeof(DerivedModel));
        Assert.ThrowsExactly<InvalidCastException>(() =>
            accessor.TryGetValue(new BaseModel(), nameof(BaseModel.BaseValue), out _)
        );
        Assert.ThrowsExactly<InvalidCastException>(() =>
            accessor.TrySetValue(new BaseModel(), nameof(BaseModel.BaseValue), "wrong")
        );
    }

    [TestMethod]
    public void RestrictedRefProperties_PreserveReadWriteAndReadOnlyCapabilities()
    {
        var accessor = TypeAccessor.Create<RestrictedRefModel>(true);
        Assert.IsFalse(
            TypeAccessor.Create(typeof(RestrictedRefModel), true).GetType().Assembly.IsDynamic
        );
        var model = new RestrictedRefModel();
        accessor[model, "Value"] = 7;
        Assert.AreEqual(7, accessor[model, "ReadOnlyValue"]);
        Assert.IsFalse(accessor.GetMemberDictionary()["ReadOnlyValue"].CanWrite);
        Assert.IsFalse(accessor.TrySetValue(model, "ReadOnlyValue", 11));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            accessor[model, "ReadOnlyValue"] = 11
        );
        Assert.AreEqual(7, model.Value);
    }

    [TestMethod]
    public void ConstructorExceptions_PropagateWithoutReflectionWrapping()
    {
        foreach (
            var type in new[]
            {
                typeof(ThrowingConstructorModel),
                typeof(RestrictedThrowingConstructorModel),
            }
        )
        {
            var accessor = TypeAccessor.Create(type);
            Assert.IsTrue(accessor.CreateNewSupported);
            var error = Assert.ThrowsExactly<InvalidOperationException>(() => accessor.CreateNew());
            Assert.AreEqual("constructor failed", error.Message);
        }
    }

    public sealed class PermissionModel
    {
        public int Value { get; set; }
        public readonly int ReadOnlyField = 1;
        public int InitOnly { get; init; } = 2;
        public int GetterOnly { get; } = 3;
        public int PrivateSet { get; private set; }
        public Span<int> Span => default;
    }

    public class RequiredModel
    {
        public required string Name { get; init; }
    }

    public sealed class InheritedRequiredModel : RequiredModel { }

    public sealed class InitializedRequiredModel : RequiredModel
    {
        [SetsRequiredMembers]
        public InitializedRequiredModel() => Name = "initialized";
    }

    public class PropertyBaseModel
    {
        public string? Name { get; set; }
    }

    public sealed class FieldHidingModel : PropertyBaseModel
    {
        public new int Name;
    }

    public class VirtualBaseModel
    {
        public virtual int Value => 10;
    }

    public sealed class VirtualDerivedModel : VirtualBaseModel
    {
        public override int Value => 20;
    }

    public sealed class ThrowingConstructorModel
    {
        public ThrowingConstructorModel() =>
            throw new InvalidOperationException("constructor failed");
    }

    private sealed class RestrictedThrowingConstructorModel
    {
        public RestrictedThrowingConstructorModel() =>
            throw new InvalidOperationException("constructor failed");
    }

    private sealed class RestrictedThrowingModel
    {
        public int Value { get; set; }
        public string Failure
        {
            get => throw new InvalidOperationException();
            set => throw new InvalidOperationException();
        }
    }

    private sealed class RestrictedRefModel
    {
        private int _value;
        public ref int Value => ref _value;
        public ref readonly int ReadOnlyValue => ref _value;
    }

    private struct RestrictedStruct
    {
        private int _value;
        public int Value
        {
            readonly get => _value;
            set => _value = value;
        }
        public readonly int ReadOnlyValue => _value;
    }
}
