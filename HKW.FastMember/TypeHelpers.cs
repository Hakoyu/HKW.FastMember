using System.Collections.Immutable;
using System.Reflection;

namespace HKW.FastMember;

/// <summary>
/// 快速成员工具
/// </summary>
public static class TypeHelpers
{
    const BindingFlags PublicInstance = BindingFlags.Public | BindingFlags.Instance;

    /// <summary>
    /// 如果类型是一个类，获取其属性；如果类型是一个接口，获取其属性以及它继承的所有接口的属性。
    /// </summary>
    /// <param name="type">类型</param>
    /// <param name="flags">标志</param>
    /// <returns>所有属性成员</returns>
    public static PropertyInfo[] GetTypeAndInterfaceProperties(this Type type, BindingFlags flags)
    {
        return !type.IsInterface
            ? type.GetProperties(flags)
            : type.GetInterfaces().Concat([type]).SelectMany(i => i.GetProperties(flags)).ToArray();
    }

    /// <summary>
    /// 获取类型的成员
    /// </summary>
    /// <param name="type">类型</param>
    /// <param name="bindingFlags">标志</param>
    /// <returns>所有目标成员</returns>
    public static Member[] GetMembers(Type type, BindingFlags bindingFlags = PublicInstance)
    {
        ArgumentNullException.ThrowIfNull(type, nameof(type));
        return type.GetTypeAndInterfaceProperties(bindingFlags)
            .Cast<MemberInfo>()
            .Concat(type.GetFields(bindingFlags).Cast<MemberInfo>())
            .OrderBy(x => x.Name)
            .Select(member => new Member(member))
            .ToArray();
    }

    /// <summary>
    /// 获取类型的成员
    /// </summary>
    /// <typeparam name="T">类型</typeparam>
    /// <param name="bindingFlags">标志</param>
    /// <returns>所有目标成员</returns>
    public static Member[] GetMembers<T>(BindingFlags bindingFlags = PublicInstance)
    {
        return GetMembers(typeof(T), bindingFlags);
    }

    /// <summary>
    /// 获取类型的成员字典 (MemberName, Member)
    /// </summary>
    /// <param name="type">类型</param>
    /// <param name="bindingFlags">标志</param>
    /// <returns>所有目标成员字典</returns>
    public static ImmutableDictionary<string, Member> GetMembersDictionary(
        this Type type,
        BindingFlags bindingFlags = PublicInstance
    )
    {
        ArgumentNullException.ThrowIfNull(type, nameof(type));
        return type.GetTypeAndInterfaceProperties(PublicInstance)
            .Cast<MemberInfo>()
            .Concat(type.GetFields(PublicInstance).Cast<MemberInfo>())
            .OrderBy(x => x.Name)
            .Select(member => new Member(member))
            .ToImmutableDictionary(x => x.Name, x => x);
    }

    /// <summary>
    /// 获取类型的成员字典 (MemberName, Member)
    /// </summary>
    /// <typeparam name="T">类型</typeparam>
    /// <param name="bindingFlags">标志</param>
    /// <returns>所有目标成员字典</returns>
    public static ImmutableDictionary<string, Member> GetMembersDictionary<T>(
        BindingFlags bindingFlags = PublicInstance
    )
    {
        return GetMembersDictionary(typeof(T));
    }
}
