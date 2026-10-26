using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace HKW.FastMember;

/// <summary>
/// 仅基于反射的元数据查询; BindingFlags 可指定超出访问器公开实例成员范围的查询
/// </summary>
public static class TypeHelpers
{
    private const BindingFlags PublicInstance = BindingFlags.Public | BindingFlags.Instance;

    /// <summary>
    /// 返回反射发现的属性, 包括继承的接口属性
    /// </summary>
    [RequiresUnreferencedCode(DeploymentRequirements.UnreferencedCode)]
    public static PropertyInfo[] GetTypeAndInterfaceProperties(this Type type, BindingFlags flags)
    {
        ArgumentNullException.ThrowIfNull(type);
        return type.IsInterface is false
            ? type.GetProperties(flags)
            : new[] { type }
                .Concat(
                    type.GetInterfaces()
                        .OrderByDescending(t => t.GetInterfaces().Length)
                        .ThenBy(t => t.FullName, StringComparer.Ordinal)
                )
                .SelectMany(t => t.GetProperties(flags))
                .ToArray();
    }

    /// <summary>
    /// 返回与指定标志匹配、经过排序和去重的反射描述
    /// </summary>
    [RequiresUnreferencedCode(DeploymentRequirements.UnreferencedCode)]
    public static ImmutableArray<ReflectionMember> GetMembers(
        Type type,
        BindingFlags bindingFlags = PublicInstance
    ) =>
        DiscoverMembers(type, bindingFlags, (bindingFlags & BindingFlags.NonPublic) != 0)
            .ToImmutableArray();

    /// <summary>
    /// 返回类型 T 中与指定标志匹配的反射描述
    /// </summary>
    [RequiresUnreferencedCode(DeploymentRequirements.UnreferencedCode)]
    public static IEnumerable<ReflectionMember> GetMembers<T>(
        BindingFlags bindingFlags = PublicInstance
    ) => GetMembers(typeof(T), bindingFlags);

    /// <summary>
    /// 返回以成员名称为键的描述字典, 名称按序号比较并区分大小写
    /// </summary>
    [RequiresUnreferencedCode(DeploymentRequirements.UnreferencedCode)]
    public static ImmutableDictionary<string, ReflectionMember> GetMembersDictionary(
        this Type type,
        BindingFlags bindingFlags = PublicInstance
    ) =>
        GetMembers(type, bindingFlags)
            .ToImmutableDictionary(m => m.Name, m => m, StringComparer.Ordinal);

    /// <summary>
    /// 返回类型 <typeparamref name="T"/> 的描述字典, 以成员名称为键, 名称按序号比较并区分大小写
    /// </summary>
    [RequiresUnreferencedCode(DeploymentRequirements.UnreferencedCode)]
    public static ImmutableDictionary<string, ReflectionMember> GetMembersDictionary<T>(
        BindingFlags bindingFlags = PublicInstance
    ) => GetMembersDictionary(typeof(T), bindingFlags);

    [RequiresUnreferencedCode(DeploymentRequirements.UnreferencedCode)]
    internal static IEnumerable<ReflectionMember> DiscoverMembers(
        Type type,
        BindingFlags flags,
        bool nonPublic
    )
    {
        ArgumentNullException.ThrowIfNull(type);
        return type.GetTypeAndInterfaceProperties(flags)
            .Where(p => p.GetIndexParameters().Length == 0)
            .Cast<MemberInfo>()
            .Concat(type.GetFields(flags))
            .OrderBy(m => DeclaringDistance(type, m.DeclaringType!))
            .ThenBy(m => m.DeclaringType!.FullName, StringComparer.Ordinal)
            .GroupBy(m => m.Name, StringComparer.Ordinal)
            .Select(g => g.First())
            .OrderBy(m => m.Name, StringComparer.Ordinal)
            .Select(m => new ReflectionMember(m, nonPublic));
    }

    [RequiresUnreferencedCode(DeploymentRequirements.UnreferencedCode)]
    private static int DeclaringDistance(Type type, Type declaring)
    {
        if (type == declaring)
            return 0;
        if (type.IsInterface)
            return 1 + type.GetInterfaces().Length - declaring.GetInterfaces().Length;
        var distance = 0;
        for (var current = type; current is not null; current = current.BaseType, distance++)
            if (current == declaring)
                return distance;
        return int.MaxValue;
    }
}
