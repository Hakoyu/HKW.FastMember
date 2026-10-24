using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Dynamic;
using System.Reflection;
using HKW.FastMember.Abstractions;
using Accessor = HKW.FastMember.Abstractions.TypeAccessor;

namespace HKW.FastMember.Reflection;

internal static class ReflectionAccessorFactory
{
    private static readonly ConcurrentDictionary<
        (Type Type, bool NonPublic),
        Lazy<Accessor>
    > Cache = new();

    [RequiresDynamicCode(DeploymentRequirements.DynamicCode)]
    [RequiresUnreferencedCode(DeploymentRequirements.UnreferencedCode)]
    internal static Accessor Create(Type type, bool nonPublic)
    {
        ArgumentNullException.ThrowIfNull(type);
        DeploymentRequirements.EnsureDynamicCodeSupported();
        if (
            type.ContainsGenericParameters
            || type.IsByRef
            || type.IsPointer
            || type.IsFunctionPointer
            || type.IsByRefLike
            || type == typeof(void)
        )
            throw new ArgumentException(
                "A closed, object-compatible target type is required.",
                nameof(type)
            );
        return Cache
            .GetOrAdd(
                (type, nonPublic),
                static key => new Lazy<Accessor>(
                    () =>
                        typeof(IDynamicMetaObjectProvider).IsAssignableFrom(key.Type)
                            ? new DynamicAccessor(key.Type)
                            : ReflectionAccessorCore.Create(
                                new ReflectionModel(key.Type, key.NonPublic)
                            ),
                    LazyThreadSafetyMode.ExecutionAndPublication
                )
            )
            .Value;
    }

    [RequiresDynamicCode(DeploymentRequirements.DynamicCode)]
    [RequiresUnreferencedCode(DeploymentRequirements.UnreferencedCode)]
    internal static TypeAccessor<T> Create<T>(bool nonPublic)
        where T : class => (nonPublic ? TypedCache<T>.NonPublic : TypedCache<T>.Public).Value;

    [RequiresDynamicCode(DeploymentRequirements.DynamicCode)]
    [RequiresUnreferencedCode(DeploymentRequirements.UnreferencedCode)]
    private static class TypedCache<T>
        where T : class
    {
        internal static readonly Lazy<TypeAccessor<T>> Public = new(() =>
            new TypedAccessor<T>(Create(typeof(T), false))
        );
        internal static readonly Lazy<TypeAccessor<T>> NonPublic = new(() =>
            new TypedAccessor<T>(Create(typeof(T), true))
        );
    }

    private sealed class TypedAccessor<T> : TypeAccessor<T>
        where T : class
    {
        private readonly Accessor _accessor;

        internal TypedAccessor(Accessor accessor)
            : base(
                accessor.GetMembersSupported ? accessor.GetMembers() : [],
                accessor.CreateNewSupported,
                accessor.ContractVersion,
                accessor.GetMembersSupported
            )
        {
            _accessor = accessor;
        }

        // 复用底层核心，通过索引分派避免再次按成员名称查找。
        protected override object? GetValueTypedCore(T target, int index) =>
            ReflectionAccessorCore.GetValue(_accessor, target, index);

        protected override void SetValueTypedCore(T target, int index, object? value) =>
            ReflectionAccessorCore.SetValue(_accessor, target, index, value);

        protected override T CreateNewTypedCore() => (T)_accessor.CreateNew();

        public override object? this[object target, string name]
        {
            get => _accessor.GetMembersSupported ? base[target, name] : _accessor[target, name];
            set
            {
                if (_accessor.GetMembersSupported)
                    base[target, name] = value;
                else
                    _accessor[target, name] = value;
            }
        }

        public override bool TryGetValue(object target, string name, out object? value) =>
            _accessor.GetMembersSupported
                ? base.TryGetValue(target, name, out value)
                : _accessor.TryGetValue(target, name, out value);

        public override bool TrySetValue(object target, string name, object? value) =>
            _accessor.GetMembersSupported
                ? base.TrySetValue(target, name, value)
                : _accessor.TrySetValue(target, name, value);
    }
}

internal sealed class ReflectionModel
{
    [RequiresUnreferencedCode(DeploymentRequirements.UnreferencedCode)]
    internal ReflectionModel(Type type, bool nonPublic)
    {
        Type = type;
        Members = TypeHelpers
            .DiscoverMembers(type, BindingFlags.Public | BindingFlags.Instance, nonPublic)
            .ToArray();
        if (type.IsClass && type.IsAbstract is false)
        {
            var constructor = type.GetConstructor(Type.EmptyTypes);
            bool required = false;
            for (var current = type; current is not null; current = current.BaseType)
                required |= current.CustomAttributes.Any(a =>
                    a.AttributeType.FullName
                    == "System.Runtime.CompilerServices.RequiredMemberAttribute"
                );
            if (
                required is false
                || constructor?.CustomAttributes.Any(a =>
                    a.AttributeType.FullName
                    == "System.Diagnostics.CodeAnalysis.SetsRequiredMembersAttribute"
                ) == true
            )
                Constructor = constructor;
        }
    }

    internal Type Type { get; }
    internal ReflectionMember[] Members { get; }
    internal ConstructorInfo? Constructor { get; }
    internal bool IsPublic =>
        Type.IsVisible
        && Members.All(m =>
            m.MemberInfo.DeclaringType!.IsVisible
            && m.Type.IsVisible
            && (m.CanRead is false || m.Getter is null || m.Getter.IsPublic)
            && (m.CanWrite is false || m.Setter is null || m.Setter.IsPublic)
        );
}
