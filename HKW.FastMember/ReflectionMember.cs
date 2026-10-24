using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using HKW.FastMember.Abstractions;

namespace HKW.FastMember;

/// <summary>
/// 公共成员元数据，附带供运行时后端使用的显式反射句柄
/// </summary>
public sealed class ReflectionMember : Member
{
    [RequiresUnreferencedCode(DeploymentRequirements.UnreferencedCode)]
    internal ReflectionMember(MemberInfo member, bool allowNonPublicAccessors)
        : base(
            member.Name,
            ValueType(member),
            GetOrdinal(member),
            CanReadMember(member, allowNonPublicAccessors),
            CanWriteMember(member, allowNonPublicAccessors),
            $"{member.DeclaringType?.FullName}.{member.Name}:{(member is FieldInfo ? "field" : "property")}",
            Nullable.GetUnderlyingType(ValueType(member)) ?? ValueType(member),
            ValueType(member).IsValueType is false
                || Nullable.GetUnderlyingType(ValueType(member)) is not null
        )
    {
        MemberInfo = member;
        if (member is PropertyInfo property)
        {
            Getter = property.GetGetMethod(allowNonPublicAccessors);
            Setter = property.PropertyType.IsByRef
                ? Getter
                : property.GetSetMethod(allowNonPublicAccessors);
        }
    }

    /// <summary>
    /// 用于构造此描述的反射句柄
    /// </summary>
    public MemberInfo MemberInfo { get; }
    internal MethodInfo? Getter { get; }
    internal MethodInfo? Setter { get; }
    internal bool IsByRef => MemberInfo is PropertyInfo property && property.PropertyType.IsByRef;

    private static Type ValueType(MemberInfo member)
    {
        var type = member is FieldInfo field
            ? field.FieldType
            : ((PropertyInfo)member).PropertyType;
        return type.IsByRef ? type.GetElementType()! : type;
    }

    private static bool Supported(MemberInfo member)
    {
        var type = ValueType(member);
        return type.IsPointer is false
            && type.IsFunctionPointer is false
            && type.IsByRefLike is false
            && type.ContainsGenericParameters is false
            && (member is not FieldInfo field || field.FieldType.IsByRef is false);
    }

    private static bool CanReadMember(MemberInfo member, bool nonPublic) =>
        Supported(member)
        && (member is FieldInfo || ((PropertyInfo)member).GetGetMethod(nonPublic) is not null);

    private static bool CanWriteMember(MemberInfo member, bool nonPublic)
    {
        if (Supported(member) is false)
            return false;
        if (member is FieldInfo field)
            return field.IsInitOnly is false && field.IsLiteral is false;
        var property = (PropertyInfo)member;
        if (property.PropertyType.IsByRef)
        {
            var getter = property.GetGetMethod(nonPublic);
            return getter is not null
                && getter.ReturnParameter.GetRequiredCustomModifiers().Any(IsReadOnlyMarker)
                    is false
                && getter.ReturnParameter.CustomAttributes.Any(a =>
                    IsReadOnlyMarker(a.AttributeType)
                )
                    is false
                && property.CustomAttributes.Any(a => IsReadOnlyMarker(a.AttributeType)) is false;
        }
        var setter = property.GetSetMethod(nonPublic);
        return setter is not null
            && setter
                .ReturnParameter.GetRequiredCustomModifiers()
                .Any(t => t.FullName == "System.Runtime.CompilerServices.IsExternalInit")
                is false;
    }

    private static bool IsReadOnlyMarker(Type type) =>
        type.FullName
            is "System.Runtime.CompilerServices.IsReadOnlyAttribute"
                or "System.Runtime.InteropServices.InAttribute";

    private static int GetOrdinal(MemberInfo member)
    {
        var attribute = member.CustomAttributes.FirstOrDefault(a =>
            a.AttributeType == typeof(OrdinalAttribute)
        );
        return attribute is null ? -1 : Convert.ToInt32(attribute.ConstructorArguments[0].Value);
    }
}
