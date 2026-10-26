using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace HKW.FastMember.Generater.SourceGenerator.Models;

internal sealed class MemberModel
{
#pragma warning disable S107
    internal MemberModel(
        string name,
        string type,
        string declaringType,
        string identity,
        int ordinal,
        bool canRead,
        bool canWrite,
        string effectiveType,
        bool allowNull
    )
#pragma warning restore S107
    {
        Name = name;
        Type = type;
        DeclaringType = declaringType;
        Identity = identity;
        Ordinal = ordinal;
        CanRead = canRead;
        CanWrite = canWrite;
        EffectiveType = effectiveType;
        AllowNull = allowNull;
    }

    internal string Name { get; }
    internal string Type { get; }
    internal string DeclaringType { get; }
    internal string Identity { get; }
    internal int Ordinal { get; }
    internal bool CanRead { get; }
    internal bool CanWrite { get; }
    internal string EffectiveType { get; }
    internal bool AllowNull { get; }
}

internal sealed class AccessorModel : IEquatable<AccessorModel>
{
    internal AccessorModel(string identity, string type, string identifier, string source)
    {
        Identity = identity;
        Type = type;
        Identifier = identifier;
        Source = source;
    }

    internal string Identity { get; }
    internal string Type { get; }
    internal string Identifier { get; }
    internal string Source { get; }

    public bool Equals(AccessorModel? other) =>
        other != null && Identity == other.Identity && Source == other.Source;

    public override bool Equals(object? obj) => Equals(obj as AccessorModel);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Identity);
}

internal sealed class AnalysisResult
{
    internal AnalysisResult(AccessorModel? model, ImmutableArray<Diagnostic> diagnostics)
    {
        Model = model;
        Diagnostics = diagnostics;
    }

    internal AccessorModel? Model { get; }
    internal ImmutableArray<Diagnostic> Diagnostics { get; }
}
