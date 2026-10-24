using System.Runtime.CompilerServices;

namespace HKW.FastMember;

internal static class DeploymentRequirements
{
    internal const string DynamicCode =
        "Built-in member accessors require runtime dynamic code generation and do not support Native AOT.";

    internal const string UnreferencedCode =
        "Members discovered by name, including non-public accessors and backing fields, may be removed by trimming.";

    internal static void EnsureDynamicCodeSupported()
    {
        if (!RuntimeFeature.IsDynamicCodeSupported)
            throw new PlatformNotSupportedException(DynamicCode);
    }
}
