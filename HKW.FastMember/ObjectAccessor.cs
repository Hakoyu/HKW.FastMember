using System.Diagnostics.CodeAnalysis;

namespace HKW.FastMember;

/// <summary>
/// 使用反射创建公共对象包装器; 泛型重载采用静态类型 T 的契约
/// </summary>
public static class ObjectAccessor
{
    /// <summary>
    /// 根据源对象的运行时类型创建包装器，支持动态绑定
    /// </summary>
    [RequiresDynamicCode(DeploymentRequirements.DynamicCode)]
    [RequiresUnreferencedCode(DeploymentRequirements.UnreferencedCode)]
    public static Abstractions.ObjectAccessor Create(object source) => Create(source, false);

    /// <summary>
    /// 根据源对象的运行时类型及所选访问器可见性创建包装器
    /// </summary>
    [RequiresDynamicCode(DeploymentRequirements.DynamicCode)]
    [RequiresUnreferencedCode(DeploymentRequirements.UnreferencedCode)]
    public static Abstractions.ObjectAccessor Create(object source, bool allowNonPublicAccessors)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new(source, TypeAccessor.Create(source.GetType(), allowNonPublicAccessors));
    }

    /// <summary>
    /// 根据静态类型 T 的契约及所选访问器可见性创建源对象包装器
    /// </summary>
    [RequiresDynamicCode(DeploymentRequirements.DynamicCode)]
    [RequiresUnreferencedCode(DeploymentRequirements.UnreferencedCode)]
    public static Abstractions.ObjectAccessor<T> Create<T>(
        T source,
        bool allowNonPublicAccessors = false
    )
        where T : class
    {
        ArgumentNullException.ThrowIfNull(source);
        return new(source, TypeAccessor.Create<T>(allowNonPublicAccessors));
    }
}
