using HKW.FastMember.Abstractions;

namespace HKW.FastMember.Generater;

internal static class GeneratedAccessorContractComparer
{
    internal static bool HasSameContract<T>(TypeAccessor<T> first, TypeAccessor<T> second)
        where T : class
    {
        if (
            first.TargetType != second.TargetType
            || first.GetMembersSupported != second.GetMembersSupported
            || first.ContractVersion != second.ContractVersion
            || first.CreateNewSupported != second.CreateNewSupported
        )
            return false;
        var firstMembers = first.GetMembers();
        var secondMembers = second.GetMembers();
        if (firstMembers.Count != secondMembers.Count)
            return false;
        for (var index = 0; index < firstMembers.Count; index++)
        {
            var left = firstMembers[index];
            var right = secondMembers[index];
            if (
                left.Name != right.Name
                || left.Type != right.Type
                || left.Ordinal != right.Ordinal
                || left.CanRead != right.CanRead
                || left.CanWrite != right.CanWrite
                || left.DeclaringIdentity != right.DeclaringIdentity
                || left.EffectiveType != right.EffectiveType
                || left.AllowDBNull != right.AllowDBNull
            )
                return false;
        }
        return true;
    }
}
