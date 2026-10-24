using Microsoft.CodeAnalysis;

namespace HKW.FastMember.Generater.SourceGenerator;

internal static class Diagnostics
{
    private static DiagnosticDescriptor Error(string id, string title, string message) =>
        new DiagnosticDescriptor(
            id,
            title,
            message,
            "FastMemberGeneration",
            DiagnosticSeverity.Error,
            true
        );

    internal static readonly DiagnosticDescriptor UnsupportedType = Error(
        "HFMG001",
        "Unsupported accessor type",
        "Type '{0}' cannot have a generated accessor: {1}. Keep a concrete accessible class at the Create call site."
    );
    internal static readonly DiagnosticDescriptor UnsupportedMember = Error(
        "HFMG002",
        "Unsupported member",
        "Member '{0}' cannot be represented by object-based access: {1}. Use a model with supported public members."
    );
    internal static readonly DiagnosticDescriptor InaccessibleType = Error(
        "HFMG003",
        "Type cannot be named by generated code",
        "Type '{0}' is not accessible from a generated top-level class. Expose a nameable class or move the call to an accessible assembly."
    );
    internal static readonly DiagnosticDescriptor IndirectReference = Error(
        "HFMG004",
        "Indirect factory reference is unsupported",
        "Factory '{0}' must be invoked directly with a concrete static type; replace this method group or delegate reference with a direct Create call."
    );
    internal static readonly DiagnosticDescriptor DynamicInvocation = Error(
        "HFMG005",
        "Dynamic factory invocation is unsupported",
        "Factory '{0}' cannot resolve a concrete static type from dynamic arguments. Keep the model's concrete static type."
    );
    internal static readonly DiagnosticDescriptor AmbiguousMember = Error(
        "HFMG006",
        "Ambiguous member declaration",
        "Member '{0}' has multiple eligible declarations at the same inheritance level. Use an unambiguous model."
    );
    internal static readonly DiagnosticDescriptor IllegalAccess = Error(
        "HFMG007",
        "Member cannot be emitted as legal C#",
        "Member '{0}' cannot be accessed in generated C#: {1}. Expose a callable public member."
    );
    internal static readonly DiagnosticDescriptor AmbiguousType = Error(
        "HFMG008",
        "Type identity cannot be bound uniquely",
        "Type '{0}' cannot be uniquely named using global:: in generated code. Avoid conflicting assembly aliases or duplicate full type names."
    );
}
