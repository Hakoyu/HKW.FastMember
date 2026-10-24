using System.Diagnostics.CodeAnalysis;
using Accessor = HKW.FastMember.Abstractions.TypeAccessor;

namespace HKW.FastMember.Reflection;

[RequiresDynamicCode(DeploymentRequirements.DynamicCode)]
[RequiresUnreferencedCode(DeploymentRequirements.UnreferencedCode)]
internal sealed class DynamicAccessor(Type type)
    : Accessor(type, [], false, getMembersSupported: false)
{
    public override object? this[object target, string name]
    {
        get
        {
            Validate(target, name);
            return CallSiteCache.GetValue(name, target);
        }
        set
        {
            Validate(target, name);
            CallSiteCache.SetValue(name, target, value!);
        }
    }

    public override bool TryGetValue(object target, string name, out object? value)
    {
        Validate(target, name);
        return CallSiteCache.TryGetValue(name, target, out value);
    }

    public override bool TrySetValue(object target, string name, object? value)
    {
        Validate(target, name);
        return CallSiteCache.TrySetValue(name, target, value);
    }

    private void Validate(object target, string name)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(name);
        if (TargetType.IsInstanceOfType(target) is false)
            throw new InvalidCastException();
    }
}
