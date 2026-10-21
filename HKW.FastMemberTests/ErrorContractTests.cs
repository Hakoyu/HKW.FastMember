using HKW.FastMember;

namespace HKW.FastMemberTests;

[TestClass]
public sealed class ErrorContractTests
{
    [TestMethod]
    public void NullInputs_AreRejectedByPublicFactoriesAndAccessors()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => TypeAccessor.Create(null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => ObjectAccessor.Create(null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => ObjectAccessor.Create(null!, true));

        var typeAccessor = TypeAccessor.Create(typeof(MemberModel));
        var objectAccessor = ObjectAccessor.Create(new MemberModel());
        var model = new MemberModel();

        Assert.ThrowsExactly<NullReferenceException>(() =>
            _ = typeAccessor[null!, nameof(MemberModel.Name)]
        );
        Assert.ThrowsExactly<ArgumentNullException>(() =>
            typeAccessor.TryGetValue(model, null!, out _)
        );
        Assert.ThrowsExactly<ArgumentNullException>(() =>
            typeAccessor.TrySetValue(model, null!, "value")
        );
        Assert.ThrowsExactly<ArgumentNullException>(() => objectAccessor.TryGetValue(null!, out _));
        Assert.ThrowsExactly<ArgumentNullException>(() =>
            objectAccessor.TrySetValue(null!, "value")
        );
    }

    [TestMethod]
    public void UnknownMembers_ThrowForIndexersAndReturnFalseForTryMethods()
    {
        var model = new MemberModel();
        var typeAccessor = TypeAccessor.Create(typeof(MemberModel));
        var objectAccessor = ObjectAccessor.Create(model);

        var typeGet = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            _ = typeAccessor[model, "Missing"]
        );
        var typeSet = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            typeAccessor[model, "Missing"] = 1
        );
        var objectGet = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            _ = objectAccessor["Missing"]
        );
        var objectSet = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            objectAccessor["Missing"] = 1
        );

        Assert.AreEqual("name", typeGet.ParamName);
        Assert.AreEqual("name", typeSet.ParamName);
        Assert.AreEqual("name", objectGet.ParamName);
        Assert.AreEqual("name", objectSet.ParamName);
        Assert.IsFalse(typeAccessor.TryGetValue(model, "Missing", out var typeValue));
        Assert.IsNull(typeValue);
        Assert.IsFalse(typeAccessor.TrySetValue(model, "Missing", 1));
        Assert.IsFalse(objectAccessor.TryGetValue("Missing", out var objectValue));
        Assert.IsNull(objectValue);
        Assert.IsFalse(objectAccessor.TrySetValue("Missing", 1));
    }

    [TestMethod]
    public void InvalidTargetsAndValues_PropagateRuntimeTypeErrorsWithoutMutation()
    {
        var model = new MemberModel { Number = 7 };
        var typeAccessor = TypeAccessor.Create(typeof(MemberModel));
        var objectAccessor = ObjectAccessor.Create(model);

        Assert.ThrowsExactly<InvalidCastException>(() =>
            _ = typeAccessor[new object(), nameof(MemberModel.Name)]
        );
        Assert.ThrowsExactly<InvalidCastException>(() =>
            typeAccessor[model, nameof(MemberModel.Number)] = "wrong"
        );
        Assert.ThrowsExactly<InvalidCastException>(() =>
            typeAccessor.TrySetValue(model, nameof(MemberModel.Number), "wrong")
        );
        Assert.ThrowsExactly<NullReferenceException>(() =>
            typeAccessor[model, nameof(MemberModel.Number)] = null!
        );
        Assert.ThrowsExactly<InvalidCastException>(() =>
            objectAccessor[nameof(MemberModel.Number)] = "wrong"
        );
        Assert.ThrowsExactly<NullReferenceException>(() =>
            objectAccessor.TrySetValue(nameof(MemberModel.Number), null!)
        );
        Assert.AreEqual(7, model.Number);
    }

    [TestMethod]
    public void CreateNew_ThrowsWhenConstructionIsUnsupported()
    {
        var noDefaultConstructor = TypeAccessor.Create(typeof(NoDefaultConstructor));
        var interfaceAccessor = TypeAccessor.Create(typeof(IDerivedContract));
        var abstractAccessor = TypeAccessor.Create(typeof(AbstractModel));

        Assert.IsFalse(noDefaultConstructor.CreateNewSupported);
        Assert.IsFalse(interfaceAccessor.CreateNewSupported);
        Assert.IsFalse(abstractAccessor.CreateNewSupported);
        Assert.ThrowsExactly<NotSupportedException>(() => noDefaultConstructor.CreateNew());
        Assert.ThrowsExactly<NotSupportedException>(() => interfaceAccessor.CreateNew());
        Assert.ThrowsExactly<NotSupportedException>(() => abstractAccessor.CreateNew());
    }

    [TestMethod]
    public void ReadOnlyAndWriteOnlyOperations_HaveConsistentTryAndIndexerContracts()
    {
        var model = new MemberModel();
        var accessor = TypeAccessor.Create(typeof(MemberModel));

        Assert.IsFalse(
            accessor.TrySetValue(model, nameof(MemberModel.ReadOnlyProperty), "changed")
        );
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            accessor[model, nameof(MemberModel.ReadOnlyProperty)] = "changed"
        );
        Assert.AreEqual("read-only", accessor[model, nameof(MemberModel.ReadOnlyProperty)]);

        Assert.IsTrue(
            accessor.TrySetValue(model, nameof(MemberModel.WriteOnlyProperty), "written")
        );
        Assert.AreEqual("written", model.GetWriteOnlyValue());
        Assert.IsFalse(
            accessor.TryGetValue(model, nameof(MemberModel.WriteOnlyProperty), out var value)
        );
        Assert.IsNull(value);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            _ = accessor[model, nameof(MemberModel.WriteOnlyProperty)]
        );
    }

    [TestMethod]
    public void UserGetterAndSetterExceptions_PropagateUnwrappedAcrossPublicApis()
    {
        var model = new ThrowingAccessorModel();
        var typeAccessor = TypeAccessor.Create(typeof(ThrowingAccessorModel));
        var objectAccessor = ObjectAccessor.Create(model);

        AssertUserException(
            () => _ = typeAccessor[model, nameof(ThrowingAccessorModel.ThrowingGetter)],
            "getter failed"
        );
        AssertUserException(
            () =>
                typeAccessor.TryGetValue(
                    model,
                    nameof(ThrowingAccessorModel.ThrowingGetter),
                    out _
                ),
            "getter failed"
        );
        AssertUserException(
            () => typeAccessor[model, nameof(ThrowingAccessorModel.ThrowingSetter)] = "value",
            "setter failed"
        );
        AssertUserException(
            () =>
                typeAccessor.TrySetValue(
                    model,
                    nameof(ThrowingAccessorModel.ThrowingSetter),
                    "value"
                ),
            "setter failed"
        );
        AssertUserException(
            () => _ = objectAccessor[nameof(ThrowingAccessorModel.ThrowingGetter)],
            "getter failed"
        );
        AssertUserException(
            () => objectAccessor.TryGetValue(nameof(ThrowingAccessorModel.ThrowingGetter), out _),
            "getter failed"
        );
        AssertUserException(
            () => objectAccessor[nameof(ThrowingAccessorModel.ThrowingSetter)] = "value",
            "setter failed"
        );
        AssertUserException(
            () => objectAccessor.TrySetValue(nameof(ThrowingAccessorModel.ThrowingSetter), "value"),
            "setter failed"
        );
    }

    private static void AssertUserException(Action action, string expectedMessage)
    {
        var exception = Assert.ThrowsExactly<InvalidOperationException>(action);
        Assert.AreEqual(expectedMessage, exception.Message);
    }

    private sealed class NoDefaultConstructor
    {
        public NoDefaultConstructor(int value) => Value = value;

        public int Value { get; }
    }

    private abstract class AbstractModel { }
}
