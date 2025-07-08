using System.Collections.Frozen;
using System.Reflection;

namespace HKW.FastMember;

/// <summary>
/// 表示为类型定义的单个成员的抽象视图
/// </summary>
public sealed class Member
{
    /// <summary>
    /// 此成员在其他成员中的序号。
    /// 如果未设置序号，则返回 -1。
    /// </summary>
    public int Ordinal
    {
        get
        {
            var ordinalAttr = _member.CustomAttributes.FirstOrDefault(p =>
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
    public string Name => _member.Name;

    /// <summary>
    /// 存储在此成员中的值的类型
    /// </summary>
    public Type Type
    {
        get
        {
            if (_member is FieldInfo field)
                return field.FieldType;
            if (_member is PropertyInfo property)
                return property.PropertyType;
            throw new NotSupportedException(_member.GetType().Name);
        }
    }

    /// <summary>
    /// 属性是否可写
    /// </summary>
    public bool CanWrite
    {
        get
        {
            return _member.MemberType switch
            {
                MemberTypes.Property => ((PropertyInfo)_member).CanWrite,
                _ => throw new NotSupportedException(_member.MemberType.ToString()),
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
            return _member.MemberType switch
            {
                MemberTypes.Property => ((PropertyInfo)_member).CanRead,
                _ => throw new NotSupportedException(_member.MemberType.ToString()),
            };
        }
    }

    private readonly MemberInfo _member;

    internal Member(MemberInfo member)
    {
        _member = member;
    }

    /// <summary>
    /// 指定的特性是否在此类型上定义
    /// </summary>
    public bool IsDefined(Type attributeType)
    {
        if (attributeType == null)
            throw new ArgumentNullException(nameof(attributeType));
        return Attribute.IsDefined(_member, attributeType);
    }

    /// <summary>
    /// 获取特性类型
    /// </summary>
    public Attribute GetAttribute(Type attributeType, bool inherit) =>
        Attribute.GetCustomAttribute(_member, attributeType, inherit)!;
}
