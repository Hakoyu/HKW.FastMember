using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Member = HKW.FastMember.Abstractions.Member;

namespace HKW.FastMember.Generater.Tests;

[TestClass]
public sealed class RegistryTests
{
    [TestMethod]
    public void MissingRegistrationExplainsConcreteCallAndGenerator()
    {
        var error = Assert.ThrowsExactly<InvalidOperationException>(
            GeneratedAccessorRegistry<Box<Missing>>.GetAccessor
        );
        StringAssert.Contains(error.Message, nameof(Missing));
        StringAssert.Contains(error.Message, "concrete-type");
        StringAssert.Contains(error.Message, "generator is enabled");
    }

    [TestMethod]
    public void EquivalentRegistrationIsIdempotentAcrossImplementationTypes()
    {
        var first = new TestAccessor<Equivalent>();
        GeneratedAccessorRegistry<Box<Equivalent>>.Register(first);
        GeneratedAccessorRegistry<Box<Equivalent>>.Register(first);
        GeneratedAccessorRegistry<Box<Equivalent>>.Register(new OtherAccessor<Equivalent>());
        Assert.AreSame(first, GeneratedAccessorRegistry<Box<Equivalent>>.GetAccessor());
    }

    [TestMethod]
    public void EveryContractComponentParticipatesInConflictDetection()
    {
        var first = new TestAccessor<Conflict>();
        GeneratedAccessorRegistry<Box<Conflict>>.Register(first);
        global::HKW.FastMember.Abstractions.TypeAccessor<Box<Conflict>>[] conflicts =
        [
            new TestAccessor<Conflict>([Describe(name: "Other")]),
            new TestAccessor<Conflict>([Describe(type: typeof(long))]),
            new TestAccessor<Conflict>([Describe(ordinal: 7)]),
            new TestAccessor<Conflict>([Describe(canRead: false)]),
            new TestAccessor<Conflict>([Describe(canWrite: false)]),
            new TestAccessor<Conflict>([Describe(identity: "Different.Declaration")]),
            new TestAccessor<Conflict>([Describe(effectiveType: typeof(long))]),
            new TestAccessor<Conflict>([Describe(allowDBNull: true)]),
            new TestAccessor<Conflict>([], createNewSupported: true),
            new TestAccessor<Conflict>(createNewSupported: false),
            new TestAccessor<Conflict>(contractVersion: 2),
        ];
        foreach (var conflicting in conflicts)
        {
            var error = Assert.ThrowsExactly<InvalidOperationException>(() =>
                GeneratedAccessorRegistry<Box<Conflict>>.Register(conflicting)
            );
            StringAssert.Contains(error.Message, "Conflicting");
            Assert.AreSame(first, GeneratedAccessorRegistry<Box<Conflict>>.GetAccessor());
        }
    }

    [TestMethod]
    public void NullRegistrationDoesNotOccupySlot()
    {
        var error = Assert.ThrowsExactly<ArgumentNullException>(() =>
            GeneratedAccessorRegistry<Box<NullRegistration>>.Register(null!)
        );
        Assert.AreEqual("accessor", error.ParamName);
        Assert.ThrowsExactly<InvalidOperationException>(
            GeneratedAccessorRegistry<Box<NullRegistration>>.GetAccessor
        );
        var valid = new TestAccessor<NullRegistration>();
        GeneratedAccessorRegistry<Box<NullRegistration>>.Register(valid);
        Assert.AreSame(valid, GeneratedAccessorRegistry<Box<NullRegistration>>.GetAccessor());
    }

    [TestMethod]
    public void ConcurrentEquivalentRegistrationsSafelyPublishOneCompleteAccessor()
    {
        var observed =
            new ConcurrentBag<global::HKW.FastMember.Abstractions.TypeAccessor<Box<Concurrent>>>();
        Parallel.For(
            0,
            128,
            index =>
            {
                GeneratedAccessorRegistry<Box<Concurrent>>.Register(new TestAccessor<Concurrent>());
                var accessor = GeneratedAccessorRegistry<Box<Concurrent>>.GetAccessor();
                var target = new Box<Concurrent> { Value = index };
                Assert.AreEqual(index, accessor[target, "Value"]);
                Assert.IsTrue(accessor.GetMembers()[0].CanRead);
                observed.Add(accessor);
            }
        );
        var installed = GeneratedAccessorRegistry<Box<Concurrent>>.GetAccessor();
        foreach (var accessor in observed)
            Assert.AreSame(installed, accessor);
    }

    [TestMethod]
    public void RacingConflictingContractsCannotBothWin()
    {
        using var barrier = new Barrier(2);
        var successes =
            new ConcurrentBag<global::HKW.FastMember.Abstractions.TypeAccessor<Box<Racing>>>();
        var failures = new ConcurrentBag<Exception>();
        var first = new TestAccessor<Racing>();
        var second = new TestAccessor<Racing>([Describe(ordinal: 3)]);
        void Register(global::HKW.FastMember.Abstractions.TypeAccessor<Box<Racing>> accessor)
        {
            barrier.SignalAndWait();
            try
            {
                GeneratedAccessorRegistry<Box<Racing>>.Register(accessor);
                successes.Add(accessor);
            }
            catch (InvalidOperationException exception)
            {
                failures.Add(exception);
            }
        }
        Parallel.Invoke(() => Register(first), () => Register(second));
        Assert.AreEqual(1, successes.Count);
        Assert.AreEqual(1, failures.Count);
        Assert.AreSame(successes.Single(), GeneratedAccessorRegistry<Box<Racing>>.GetAccessor());
    }

    [TestMethod]
    public void MetadataIsDefensivelyCopiedAndReadOnly()
    {
        var descriptor = Describe();
        Member[] input = [descriptor];
        var accessor = new TestAccessor<Metadata>(input);
        input[0] = Describe(name: "Changed");
        Assert.AreSame(descriptor, accessor.GetMembers()[0]);
        Assert.AreSame(descriptor, accessor.GetMemberDictionary()["Value"]);
        Assert.ThrowsExactly<NotSupportedException>(() =>
            ((IList<Member>)accessor.GetMembers())[0] = input[0]
        );
        Assert.ThrowsExactly<NotSupportedException>(() =>
            ((IDictionary<string, Member>)accessor.GetMemberDictionary()).Clear()
        );
        Assert.AreEqual(typeof(int), descriptor.EffectiveType);
        Assert.IsFalse(descriptor.AllowDBNull);
    }

    [TestMethod]
    public void DescriptorOrderAndDuplicateNamesAreValidated()
    {
        Assert.ThrowsExactly<ArgumentException>(() =>
            new TestAccessor<Metadata>([Describe(name: "Z"), Describe(name: "A")])
        );
        Assert.ThrowsExactly<ArgumentException>(() =>
            new TestAccessor<Metadata>([Describe(), Describe()])
        );
        Assert.ThrowsExactly<ArgumentNullException>(() => new TestAccessor<Metadata>([null!]));
    }

    [TestMethod]
    public void AccessorNullAndMissingMemberContractsAreCentralized()
    {
        var accessor = new TestAccessor<Access>();
        var target = new Box<Access>();
        Assert.AreEqual(
            "name",
            Assert
                .ThrowsExactly<ArgumentNullException>(() =>
                    accessor.TryGetValue(target, null!, out _)
                )
                .ParamName
        );
        Assert.AreEqual(
            "target",
            Assert
                .ThrowsExactly<ArgumentNullException>(() => accessor.TrySetValue(null!, "Value", 1))
                .ParamName
        );
        Assert.IsFalse(accessor.TryGetValue(target, "value", out var missing));
        Assert.IsNull(missing);
        Assert.IsFalse(accessor.TrySetValue(target, "Missing", 1));
        Assert.AreEqual(
            "name",
            Assert
                .ThrowsExactly<ArgumentOutOfRangeException>(() => accessor[target, "Missing"])
                .ParamName
        );
        Assert.AreEqual(
            "name",
            Assert
                .ThrowsExactly<ArgumentOutOfRangeException>(() => accessor[target, "Missing"] = 1)
                .ParamName
        );
        Assert.IsTrue(accessor.TrySetValue(target, "Value", 42));
        Assert.IsTrue(accessor.TryGetValue(target, "Value", out var value));
        Assert.AreEqual(42, value);
    }

    [TestMethod]
    public void PermissionsAreEnforcedBeforeCoreDispatch()
    {
        var accessor = new TestAccessor<Access>([Describe(canRead: false, canWrite: false)]);
        var target = new Box<Access>();
        Assert.IsFalse(accessor.TryGetValue(target, "Value", out var value));
        Assert.IsNull(value);
        Assert.IsFalse(accessor.TrySetValue(target, "Value", 3));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => accessor[target, "Value"]);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => accessor[target, "Value"] = 3);
    }

    [TestMethod]
    public void ConversionAndUserExceptionsAreNotSwallowedByTry()
    {
        var accessor = new TestAccessor<Access>();
        var target = new Box<Access>();
        Assert.ThrowsExactly<InvalidCastException>(() =>
            accessor.TrySetValue(target, "Value", "wrong")
        );
        var expected = new InvalidOperationException("User getter failed");
        target.Failure = expected;
        Assert.AreSame(
            expected,
            Assert.ThrowsExactly<InvalidOperationException>(() =>
                accessor.TryGetValue(target, "Value", out _)
            )
        );
        Assert.AreSame(
            expected,
            Assert.ThrowsExactly<InvalidOperationException>(() =>
                accessor.TrySetValue(target, "Value", 1)
            )
        );
    }

    [TestMethod]
    public void ConstructionFlagControlsNewAndPropagatesUserException()
    {
        var supported = new TestAccessor<Access>();
        Assert.IsTrue(supported.CreateNewSupported);
        Assert.IsNotNull(supported.CreateNew());
        var unsupported = new TestAccessor<Access>(createNewSupported: false);
        Assert.IsFalse(unsupported.CreateNewSupported);
        Assert.ThrowsExactly<NotSupportedException>(unsupported.CreateNew);
        var expected = new InvalidOperationException("User constructor failed");
        var throwing = new ThrowingConstructorAccessor(expected);
        Assert.AreSame(
            expected,
            Assert.ThrowsExactly<InvalidOperationException>(throwing.CreateNew)
        );
    }

    private static Member Describe(
        string name = "Value",
        Type? type = null,
        int ordinal = -1,
        bool canRead = true,
        bool canWrite = true,
        string identity = "RegistryTests.Box:property:Value",
        [DynamicallyAccessedMembers(
            DynamicallyAccessedMemberTypes.PublicFields
                | DynamicallyAccessedMemberTypes.PublicProperties
        )]
            Type? effectiveType = null,
        bool allowDBNull = false
    ) =>
        new(
            name,
            type ?? typeof(int),
            ordinal,
            canRead,
            canWrite,
            identity,
            effectiveType ?? typeof(int),
            allowDBNull
        );

    private sealed class Box<TMarker>
    {
        public int Value { get; set; }
        public Exception? Failure { get; set; }
    }

    private class TestAccessor<TMarker>(
        IEnumerable<Member>? members = null,
        bool createNewSupported = true,
        int contractVersion = 1
    )
        : global::HKW.FastMember.Abstractions.TypeAccessor<Box<TMarker>>(
            members ?? [Describe()],
            createNewSupported,
            contractVersion
        )
    {
        protected override object? GetValueTypedCore(Box<TMarker> target, int index)
        {
            if (target.Failure is { } failure)
                throw failure;
            return target.Value;
        }

        protected override void SetValueTypedCore(Box<TMarker> target, int index, object? value)
        {
            if (target.Failure is { } failure)
                throw failure;
            target.Value = (int)value!;
        }

        protected override Box<TMarker> CreateNewTypedCore() => new();
    }

    private sealed class OtherAccessor<TMarker> : TestAccessor<TMarker>;

    private sealed class ThrowingConstructorAccessor(Exception exception) : TestAccessor<Access>
    {
        protected override Box<Access> CreateNewTypedCore() => throw exception;
    }

    private sealed class Missing;

    private sealed class Equivalent;

    private sealed class Conflict;

    private sealed class NullRegistration;

    private sealed class Concurrent;

    private sealed class Racing;

    private sealed class Metadata;

    private sealed class Access;
}
