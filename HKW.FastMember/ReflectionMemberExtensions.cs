using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using HKW.FastMember.Abstractions;

namespace HKW.FastMember;

/// <summary>
/// 需显式使用的反射功能, 不属于独立于后端的成员契约
/// </summary>
public static class ReflectionMemberExtensions
{
    /// <summary>
    /// 获取显式反射描述；若描述来自其他后端, 则拒绝该操作
    /// </summary>
    public static ReflectionMember AsReflectionMember(this Member member)
    {
        ArgumentNullException.ThrowIfNull(member);
        return member as ReflectionMember
            ?? throw new NotSupportedException(
                "This descriptor was not produced by the reflection backend."
            );
    }

    /// <summary>
    /// 获取反射成员句柄, 不重新发现元数据
    /// </summary>
    public static MemberInfo GetMemberInfo(this Member member) =>
        member.AsReflectionMember().MemberInfo;

    /// <summary>
    /// 检查反射成员是否声明了指定特性
    /// </summary>
    [RequiresUnreferencedCode(DeploymentRequirements.UnreferencedCode)]
    public static bool IsDefined(this Member member, Type attributeType, bool inherit = false) =>
        Attribute.IsDefined(member.GetMemberInfo(), attributeType, inherit);

    /// <summary>
    /// 检查反射成员是否声明了类型为 T 的特性
    /// </summary>
    [RequiresUnreferencedCode(DeploymentRequirements.UnreferencedCode)]
    public static bool IsDefined<T>(this Member member, bool inherit = false)
        where T : Attribute => member.IsDefined(typeof(T), inherit);

    /// <summary>
    /// 从反射成员读取特性
    /// </summary>
    [RequiresUnreferencedCode(DeploymentRequirements.UnreferencedCode)]
    public static Attribute? GetAttribute(
        this Member member,
        Type attributeType,
        bool inherit = false
    ) => Attribute.GetCustomAttribute(member.GetMemberInfo(), attributeType, inherit);

    /// <summary>
    /// 从反射成员读取类型为 T 的特性
    /// </summary>
    [RequiresUnreferencedCode(DeploymentRequirements.UnreferencedCode)]
    public static T? GetAttribute<T>(this Member member, bool inherit = false)
        where T : Attribute => (T?)member.GetAttribute(typeof(T), inherit);
}
