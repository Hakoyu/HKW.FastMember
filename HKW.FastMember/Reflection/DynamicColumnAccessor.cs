using System.Diagnostics.CodeAnalysis;
using HKW.FastMember.Abstractions;
using Accessor = HKW.FastMember.Abstractions.TypeAccessor;

namespace HKW.FastMember.Reflection;

[RequiresDynamicCode(DeploymentRequirements.DynamicCode)]
[RequiresUnreferencedCode(DeploymentRequirements.UnreferencedCode)]
internal sealed class DynamicColumnAccessor : Accessor
{
    private readonly string[] _names;

    internal DynamicColumnAccessor(Type type, string[] names)
        : base(type, Describe(type, names), false)
    {
        _names = GetMembers().Select(m => m.Name).ToArray();
    }

    private static IEnumerable<Member> Describe(Type type, string[] names) =>
        names
            .Distinct(StringComparer.Ordinal)
            .OrderBy(n => n, StringComparer.Ordinal)
            .Select(n => new Member(
                n,
                typeof(object),
                -1,
                true,
                false,
                $"{type.FullName}.{n}:dynamic",
                typeof(object),
                true
            ));

    protected override object? GetValueCore(object target, int index) =>
        CallSiteCache.GetValue(_names[index], target);
}
