using System.Dynamic;

namespace HKW.FastMember;

/// <summary>
/// Represents an individual object, allowing access to members by-name
/// </summary>
public abstract class ObjectAccessor<T>
{
    /// <summary>
    /// Get or Set the value of a named member for the underlying object
    /// </summary>
    public abstract object this[string name] { get; set; }

    /// <summary>
    /// The object represented by this instance
    /// </summary>
    public T Target { get; protected set; }

    #region Create
    /// <summary>
    /// Wraps an individual object, allowing by-name access to that instance
    /// </summary>
    public static ObjectAccessor<T> Create(T target)
    {
        return Create(target, false);
    }

    /// <summary>
    /// Wraps an individual object, allowing by-name access to that instance
    /// </summary>
    /// <exception cref="ArgumentNullException">target null</exception>
    public static ObjectAccessor<T> Create(T target, bool allowNonPublicAccessors)
    {
        ArgumentNullException.ThrowIfNull(target, nameof(target));
        if (target is IDynamicMetaObjectProvider)
            return new DynamicWrapper<T>(target); // use the DLR
        return new TypeAccessorWrapper<T>(
            target,
            TypeAccessor.Create(target.GetType(), allowNonPublicAccessors)
        );
    }
    #endregion

    #region Value
    /// <summary>
    /// 获取值
    /// </summary>
    /// <typeparam name="TValue">值类型</typeparam>
    /// <param name="name">目标名称</param>
    /// <returns>值</returns>
    public TValue GetValue<TValue>(string name)
    {
        return (TValue)this[name];
    }

    /// <summary>
    /// 设置值
    /// </summary>
    /// <typeparam name="TValue">值类型</typeparam>
    /// <param name="name">名称</param>
    /// <param name="newValue">新值</param>
    public void SetValue<TValue>(string name, TValue newValue)
    {
        this[name] = newValue;
    }
    #endregion

    #region Other
    /// <summary>
    /// Use the target types definition of equality
    /// </summary>
    public override bool Equals(object obj)
    {
        return Target.Equals(obj);
    }

    /// <summary>
    /// Obtain the hash of the target object
    /// </summary>
    public override int GetHashCode()
    {
        return Target.GetHashCode();
    }

    /// <summary>
    /// Use the target's definition of a string representation
    /// </summary>
    public override string ToString()
    {
        return Target.ToString();
    }
    #endregion
}

internal sealed class TypeAccessorWrapper<T> : ObjectAccessor<T>
{
    private readonly TypeAccessor _accessor;

    public TypeAccessorWrapper(T target, TypeAccessor accessor)
    {
        Target = target;
        _accessor = accessor;
    }

    public override object this[string name]
    {
        get => _accessor[Target, name];
        set => _accessor[Target, name] = value;
    }
}

internal sealed class DynamicWrapper<T> : ObjectAccessor<T>
{
    public DynamicWrapper(T target)
    {
        Target = target;
    }

    public override object this[string name]
    {
        get => CallSiteCache.GetValue(name, Target);
        set => CallSiteCache.SetValue(name, Target, value);
    }
}
