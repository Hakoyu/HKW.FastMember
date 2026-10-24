using System.Diagnostics.CodeAnalysis;
using HKW.FastMember.Reflection;
using Accessor = HKW.FastMember.Abstractions.TypeAccessor;
using Reader = HKW.FastMember.Abstractions.ObjectReader;

namespace HKW.FastMember;

/// <summary>
/// 创建独立于后端的读取器的反射工厂, 动态数据源必须显式指定列名
/// </summary>
public static class ObjectReader
{
    /// <summary>
    /// 使用反射元数据，通过共享读取器读取同步序列
    /// </summary>
    [RequiresDynamicCode(DeploymentRequirements.DynamicCode)]
    [RequiresUnreferencedCode(DeploymentRequirements.UnreferencedCode)]
    public static Reader Create<T>(IEnumerable<T> source, params string[] members)
    {
        ArgumentNullException.ThrowIfNull(source);
        return Reader.Create(source, GetAccessor<T>(members), members);
    }

    /// <summary>
    /// 读取异步序列，创建时不指定取消令牌
    /// </summary>
    [RequiresDynamicCode(DeploymentRequirements.DynamicCode)]
    [RequiresUnreferencedCode(DeploymentRequirements.UnreferencedCode)]
    public static Reader Create<T>(IAsyncEnumerable<T> source, params string[] members) =>
        Create(source, CancellationToken.None, members);

    /// <summary>
    /// 读取异步序列，并在首次枚举时传递创建时指定的取消令牌
    /// </summary>
    [RequiresDynamicCode(DeploymentRequirements.DynamicCode)]
    [RequiresUnreferencedCode(DeploymentRequirements.UnreferencedCode)]
    public static Reader Create<T>(
        IAsyncEnumerable<T> source,
        CancellationToken cancellationToken,
        params string[] members
    )
    {
        ArgumentNullException.ThrowIfNull(source);
        return Reader.Create(source, GetAccessor<T>(members), cancellationToken, members);
    }

    [RequiresDynamicCode(DeploymentRequirements.DynamicCode)]
    [RequiresUnreferencedCode(DeploymentRequirements.UnreferencedCode)]
    private static Accessor GetAccessor<T>(string[] members)
    {
        ArgumentNullException.ThrowIfNull(members);
        foreach (var name in members)
            ArgumentNullException.ThrowIfNull(name);
        var accessor = TypeAccessor.Create(typeof(T));
        if (accessor.GetMembersSupported)
            return accessor;
        if (members.Length == 0)
            throw new InvalidOperationException("Dynamic sources require explicit column names.");
        return new DynamicColumnAccessor(typeof(T), members);
    }
}
