using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Microsoft.CSharp.RuntimeBinder;

namespace HKW.FastMember;

/// <summary>
/// 提供动态成员访问的调用站点缓存功能
/// 该类使用动态绑定技术实现对对象属性和字段的高效访问
/// </summary>
[RequiresDynamicCode(DeploymentRequirements.DynamicCode)]
[RequiresUnreferencedCode(DeploymentRequirements.UnreferencedCode)]
internal static class CallSiteCache
{
    /// <summary>
    /// 存储属性/字段获取器的缓存字典
    /// 键为成员名称，值为用于获取成员值的调用站点
    /// </summary>
    private static readonly ConcurrentDictionary<
        string,
        CallSite<Func<CallSite, object, object>>
    > _getters = [];

    /// <summary>
    /// 存储属性/字段设置器的缓存字典
    /// 键为成员名称，值为用于设置成员值的调用站点
    /// </summary>
    private static readonly ConcurrentDictionary<
        string,
        CallSite<Func<CallSite, object, object, object>>
    > _setters = [];

    /// <summary>
    /// 获取对象指定成员的值
    /// </summary>
    /// <param name="name">要获取的成员名称</param>
    /// <param name="target">目标对象实例</param>
    /// <param name="value">成员值，或绑定失败时的 null</param>
    /// <returns>绑定是否成功</returns>
    internal static bool TryGetValue(string name, object target, out object? value)
    {
        try
        {
            value = GetValue(name, target);
            return true;
        }
        catch (RuntimeBinderException)
        {
            value = null;
            return false;
        }
    }

    internal static bool TrySetValue(string name, object target, object? value)
    {
        try
        {
            SetValue(name, target, value!);
            return true;
        }
        catch (RuntimeBinderException)
        {
            return false;
        }
    }

    internal static object GetValue(string name, object target)
    {
        var callSite = _getters.GetOrAdd(
            name,
            static memberName =>
                CallSite<Func<CallSite, object, object>>.Create(
                    Binder.GetMember(
                        CSharpBinderFlags.None,
                        memberName,
                        typeof(CallSiteCache),
                        [CSharpArgumentInfo.Create(CSharpArgumentInfoFlags.None, null)]
                    )
                )
        );
        return callSite.Target(callSite, target);
    }

    /// <summary>
    /// 设置对象指定成员的值
    /// </summary>
    /// <param name="name">要设置的成员名称</param>
    /// <param name="target">目标对象实例</param>
    /// <param name="value">要设置的值</param>
    internal static void SetValue(string name, object target, object value)
    {
        var callSite = _setters.GetOrAdd(
            name,
            static memberName =>
                CallSite<Func<CallSite, object, object, object>>.Create(
                    Binder.SetMember(
                        CSharpBinderFlags.None,
                        memberName,
                        typeof(CallSiteCache),
                        [
                            CSharpArgumentInfo.Create(CSharpArgumentInfoFlags.None, null),
                            CSharpArgumentInfo.Create(
                                CSharpArgumentInfoFlags.UseCompileTimeType,
                                null
                            ),
                        ]
                    )
                )
        );
        callSite.Target(callSite, target, value);
    }
}
