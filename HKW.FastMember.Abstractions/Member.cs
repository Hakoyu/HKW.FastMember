using System.Diagnostics.CodeAnalysis;

namespace HKW.FastMember.Abstractions;

/// <summary>
/// 不可变的成员及读取器架构元数据, 不依赖后端反射句柄
/// </summary>
public class Member
{
    /// <summary>
    /// 根据访问器后端提供的元数据创建描述符
    /// </summary>
    /// <param name="name">成员名称</param>
    /// <param name="type">声明的成员类型</param>
    /// <param name="ordinal">显式指定的序号, 或 -1</param>
    /// <param name="canRead">是否支持读取</param>
    /// <param name="canWrite">是否支持写入</param>
    /// <param name="declaringIdentity">稳定的声明类型/成员标识, 包含成员种类</param>
    /// <param name="effectiveType">由后端解包 Nullable&lt;T&gt; 后的声明类型</param>
    /// <param name="allowDBNull">对于引用类型和可空值类型为 true, 不受可空引用类型（NRT）注解影响</param>
#pragma warning disable S107
    public Member(
        string name,
        Type type,
        int ordinal,
        bool canRead,
        bool canWrite,
        string declaringIdentity,
        [DynamicallyAccessedMembers(
            DynamicallyAccessedMemberTypes.PublicFields
                | DynamicallyAccessedMemberTypes.PublicProperties
        )]
            Type effectiveType,
        bool allowDBNull
    )
#pragma warning restore S107
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(declaringIdentity);
        ArgumentNullException.ThrowIfNull(effectiveType);
        Name = name;
        Type = type;
        Ordinal = ordinal;
        CanRead = canRead;
        CanWrite = canWrite;
        DeclaringIdentity = declaringIdentity;
        EffectiveType = effectiveType;
        AllowDBNull = allowDBNull;
    }

    public string Name { get; }
    public Type Type { get; }
    public int Ordinal { get; }
    public bool CanRead { get; }
    public bool CanWrite { get; }
    public string DeclaringIdentity { get; }

    [DynamicallyAccessedMembers(
        DynamicallyAccessedMemberTypes.PublicFields
            | DynamicallyAccessedMemberTypes.PublicProperties
    )]
    public Type EffectiveType { get; }
    public bool AllowDBNull { get; }
}
