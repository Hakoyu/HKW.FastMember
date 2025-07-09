using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Microsoft.CSharp.RuntimeBinder;

namespace HKW.FastMember;

/// <summary>
/// 提供动态成员访问的调用站点缓存功能。
/// 该类使用动态绑定技术实现对对象属性和字段的高效访问。
/// </summary>
internal static class CallSiteCache
{
    /// <summary>
    /// 存储属性/字段获取器的缓存字典。
    /// 键为成员名称，值为用于获取成员值的调用站点。
    /// </summary>
    private static readonly ConcurrentDictionary<
        string,
        CallSite<Func<CallSite, object, object>>
    > _getters = [];

    /// <summary>
    /// 存储属性/字段设置器的缓存字典。
    /// 键为成员名称，值为用于设置成员值的调用站点。
    /// </summary>
    private static readonly ConcurrentDictionary<
        string,
        CallSite<Func<CallSite, object, object, object>>
    > _setters = [];

    /// <summary>
    /// 获取对象指定成员的值。
    /// </summary>
    /// <param name="name">要获取的成员名称</param>
    /// <param name="target">目标对象实例</param>
    /// <returns>成员的值</returns>
    internal static object GetValue(string name, object target)
    {
        if (_getters.TryGetValue(name, out var callSite) is false)
        {
            // 如果缓存中不存在此成员的调用站点，则创建一个新的
            _getters[name] = callSite = CallSite<Func<CallSite, object, object>>.Create(
                Binder.GetMember(
                    CSharpBinderFlags.None,
                    name,
                    typeof(CallSiteCache),
                    [CSharpArgumentInfo.Create(CSharpArgumentInfoFlags.None, null)]
                )
            );
        }
        // 使用调用站点获取目标对象上指定成员的值
        return callSite.Target(callSite, target);
    }

    /// <summary>
    /// 设置对象指定成员的值。
    /// </summary>
    /// <param name="name">要设置的成员名称</param>
    /// <param name="target">目标对象实例</param>
    /// <param name="value">要设置的值</param>
    internal static void SetValue(string name, object target, object value)
    {
        if (_setters.TryGetValue(name, out var callSite) is false)
        {
            // 如果缓存中不存在此成员的调用站点，则创建一个新的
            _setters[name] = callSite = CallSite<Func<CallSite, object, object, object>>.Create(
                Binder.SetMember(
                    CSharpBinderFlags.None,
                    name,
                    typeof(CallSiteCache),
                    [
                        CSharpArgumentInfo.Create(CSharpArgumentInfoFlags.None, null),
                        CSharpArgumentInfo.Create(CSharpArgumentInfoFlags.UseCompileTimeType, null)
                    ]
                )
            );
        }
        // 使用调用站点设置目标对象上指定成员的值
        callSite.Target(callSite, target, value);
    }
}
