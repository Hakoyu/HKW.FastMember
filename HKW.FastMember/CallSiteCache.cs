using Microsoft.CSharp.RuntimeBinder;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace HKW.FastMember;

internal static class CallSiteCache
{
    private static readonly ConcurrentDictionary<
        string,
        CallSite<Func<CallSite, object, object>>
    > _getters = new();
    private static readonly ConcurrentDictionary<
        string,
        CallSite<Func<CallSite, object, object, object>>
    > _setters = new();

    internal static object GetValue(string name, object target)
    {
        if (_getters.TryGetValue(name, out var callSite) is false)
        {
            callSite = CallSite<Func<CallSite, object, object>>.Create(
                Binder.GetMember(
                    CSharpBinderFlags.None,
                    name,
                    typeof(CallSiteCache),
                    new CSharpArgumentInfo[]
                    {
                        CSharpArgumentInfo.Create(CSharpArgumentInfoFlags.None, null)
                    }
                )
            );
        }
        return callSite.Target(callSite, target);
    }

    internal static void SetValue(string name, object target, object value)
    {
        if (_setters.TryGetValue(name, out var callSite) is false)
        {
            callSite = CallSite<Func<CallSite, object, object, object>>.Create(
                Binder.SetMember(
                    CSharpBinderFlags.None,
                    name,
                    typeof(CallSiteCache),
                    new CSharpArgumentInfo[]
                    {
                        CSharpArgumentInfo.Create(CSharpArgumentInfoFlags.None, null),
                        CSharpArgumentInfo.Create(CSharpArgumentInfoFlags.UseCompileTimeType, null)
                    }
                )
            );
        }
        callSite.Target(callSite, target, value);
    }
}
