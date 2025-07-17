using System.Collections.Frozen;
using System.Reflection;

namespace HKW.FastMember;

/// <summary>
/// 表示为类型定义的单个成员的抽象视图
/// </summary>
public sealed class Member
{
    internal Member(MemberInfo member)
    {
        MemberInfo = member;
    }

    /// <summary>
    /// 成员信息
    /// </summary>
    public MemberInfo MemberInfo { get; }

    /// <summary>
    /// 此成员在其他成员中的序号。
    /// 如果未设置序号，则返回 -1。
    /// </summary>
    public int Ordinal
    {
        get
        {
            var ordinalAttr = MemberInfo.CustomAttributes.FirstOrDefault(p =>
                p.AttributeType == typeof(OrdinalAttribute)
            );

            if (ordinalAttr == null)
            {
                return -1;
            }

            // OrdinalAttribute 类必须只有一个带单个参数的构造函数。
            return Convert.ToInt32(ordinalAttr.ConstructorArguments.Single().Value);
        }
    }

    /// <summary>
    /// 此成员的名称
    /// </summary>
    public string Name => MemberInfo.Name;

    /// <summary>
    /// 存储在此成员中的值的类型
    /// </summary>
    public Type Type
    {
        get
        {
            if (MemberInfo is FieldInfo field)
                return field.FieldType;
            if (MemberInfo is PropertyInfo property)
                return property.PropertyType;
            throw new NotSupportedException(MemberInfo.GetType().Name);
        }
    }

    /// <summary>
    /// 属性是否可写
    /// </summary>
    public bool CanWrite
    {
        get
        {
            return MemberInfo.MemberType switch
            {
                MemberTypes.Property => ((PropertyInfo)MemberInfo).CanWrite,
                _ => throw new NotSupportedException(MemberInfo.MemberType.ToString()),
            };
        }
    }

    /// <summary>
    /// 属性是否可读
    /// </summary>
    public bool CanRead
    {
        get
        {
            return MemberInfo.MemberType switch
            {
                MemberTypes.Property => ((PropertyInfo)MemberInfo).CanRead,
                _ => throw new NotSupportedException(MemberInfo.MemberType.ToString()),
            };
        }
    }

    /// <summary>
    /// 指定的特性是否在此类型上定义
    /// </summary>
    public bool IsDefined(Type attributeType)
    {
        if (attributeType == null)
            throw new ArgumentNullException(nameof(attributeType));
        return Attribute.IsDefined(MemberInfo, attributeType);
    }

    /// <summary>
    /// 获取特性类型
    /// </summary>
    public Attribute GetAttribute(Type attributeType, bool inherit) =>
        Attribute.GetCustomAttribute(MemberInfo, attributeType, inherit)!;
}
