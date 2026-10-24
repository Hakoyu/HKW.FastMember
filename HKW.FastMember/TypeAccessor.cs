using System.Diagnostics.CodeAnalysis;
using HKW.FastMember.Reflection;
using Accessor = HKW.FastMember.Abstractions.TypeAccessor;

namespace HKW.FastMember;

/// <summary>
/// 创建并缓存基于反射的公共访问器。需要运行时代码生成功能。
/// </summary>
public static class TypeAccessor
{
    /// <summary>
    /// 根据指定的运行时类型创建访问器，仅使用公开访问器。
    /// </summary>
    [RequiresDynamicCode(DeploymentRequirements.DynamicCode)]
    [RequiresUnreferencedCode(DeploymentRequirements.UnreferencedCode)]
    public static Accessor Create(Type type) => Create(type, false);

    /// <summary>
    /// 根据运行时类型及所选访问器可见性创建访问器。
    /// </summary>
    [RequiresDynamicCode(DeploymentRequirements.DynamicCode)]
    [RequiresUnreferencedCode(DeploymentRequirements.UnreferencedCode)]
    public static Accessor Create(Type type, bool allowNonPublicAccessors) =>
        ReflectionAccessorFactory.Create(type, allowNonPublicAccessors);

    /// <summary>
    /// 根据静态类型 T 的契约创建类型化访问器，不采用运行时派生类型的契约。
    /// </summary>
    [RequiresDynamicCode(DeploymentRequirements.DynamicCode)]
    [RequiresUnreferencedCode(DeploymentRequirements.UnreferencedCode)]
    public static Abstractions.TypeAccessor<T> Create<T>(bool allowNonPublicAccessors = false)
        where T : class => ReflectionAccessorFactory.Create<T>(allowNonPublicAccessors);
}
