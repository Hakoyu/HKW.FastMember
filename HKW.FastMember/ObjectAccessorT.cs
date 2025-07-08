using System.Dynamic;

namespace HKW.FastMember;

/// <summary>
/// 表示单个对象，允许通过名称访问成员
/// </summary>
public abstract class ObjectAccessor<T>
{
    /// <inheritdoc/>
    protected ObjectAccessor(T source)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
    }

    /// <summary>
    /// 获取或设置基础对象的指定名称成员的值
    /// </summary>
    public abstract object this[string name] { get; set; }

    /// <summary>
    /// 此实例所代表的对象
    /// </summary>
    public object Source { get; protected set; }

    #region Create
    /// <summary>
    /// 为对象创建访问器
    /// </summary>
    /// <param name="source">对象</param>
    /// <returns>访问器</returns>
    public static ObjectAccessor Create(T source)
    {
        return Create(source, false);
    }

    /// <summary>
    /// 包装单个对象，允许通过名称访问该实例
    /// </summary>
    /// <exception cref="ArgumentNullException">目标为空</exception>
    public static ObjectAccessor Create(T source, bool allowNonPublicAccessors)
    {
        ArgumentNullException.ThrowIfNull(source, nameof(source));
        if (source is IDynamicMetaObjectProvider dlr)
            return new DynamicWrapper(dlr); // 使用DLR
        return new TypeAccessorWrapper(
            source,
            TypeAccessor.Create(source.GetType(), allowNonPublicAccessors)
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
        this[name] = newValue!;
    }
    #endregion

    #region Other

    /// <inheritdoc/>
    public override bool Equals(object? obj)
    {
        return Source.Equals(obj);
    }

    /// <summary>
    /// 获取目标对象的哈希值
    /// </summary>
    public override int GetHashCode()
    {
        return Source.GetHashCode();
    }

    /// <inheritdoc/>
    public override string ToString()
    {
        return Source.ToString()!;
    }
    #endregion
}

internal sealed class TypeAccessorWrapper<T> : ObjectAccessor<T>
{
    private readonly TypeAccessor _accessor;

    public TypeAccessorWrapper(T source, TypeAccessor accessor)
        : base(source)
    {
        _accessor = accessor;
    }

    public override object this[string name]
    {
        get => _accessor[Source, name];
        set => _accessor[Source, name] = value;
    }
}

internal sealed class DynamicWrapper<T> : ObjectAccessor<T>
    where T : IDynamicMetaObjectProvider
{
    public DynamicWrapper(T source)
        : base(source) { }

    public override object this[string name]
    {
        get => CallSiteCache.GetValue(name, Source);
        set => CallSiteCache.SetValue(name, Source, value);
    }
}
