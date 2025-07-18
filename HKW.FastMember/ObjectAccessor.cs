using System.Dynamic;

namespace HKW.FastMember;

/// <summary>
/// 表示单个对象，允许通过名称访问成员
/// </summary>
public abstract class ObjectAccessor
{
    /// <inheritdoc/>
    protected ObjectAccessor(object source)
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
    public static ObjectAccessor Create(object source)
    {
        return Create(source, false);
    }

    /// <summary>
    /// 包装单个对象，允许通过名称访问该实例
    /// </summary>
    /// <exception cref="ArgumentNullException">目标为空</exception>
    public static ObjectAccessor Create(object source, bool allowNonPublicAccessors)
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

    #region Try

    /// <summary>
    /// 尝试获取成员值
    /// </summary>
    /// <param name="name">成员名称</param>
    /// <param name="value">成员值</param>
    /// <returns>成功为 <see langword="true"/>, 失败为 <see langword="false"/></returns>
    public abstract bool TryGetValue(string name, out object value);

    /// <summary>
    /// 尝试设置成员值
    /// </summary>
    /// <param name="name">成员名称</param>
    /// <param name="value">成员值</param>
    /// <returns>成功为 <see langword="true"/>, 失败为 <see langword="false"/></returns>
    public abstract bool TrySetValue(string name, object value);
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

internal sealed class TypeAccessorWrapper : ObjectAccessor
{
    private readonly TypeAccessor _accessor;

    public TypeAccessorWrapper(object source, TypeAccessor accessor)
        : base(source)
    {
        _accessor = accessor;
    }

    public override object this[string name]
    {
        get => _accessor[Source, name];
        set => _accessor[Source, name] = value;
    }

    public override bool TryGetValue(string name, out object value)
    {
        return _accessor.TryGetValue(Source, name, out value);
    }

    public override bool TrySetValue(string name, object value)
    {
        return _accessor.TrySetValue(Source, name, value);
    }
}

internal sealed class DynamicWrapper : ObjectAccessor
{
    public DynamicWrapper(IDynamicMetaObjectProvider source)
        : base(source) { }

    public override object this[string name]
    {
        get => CallSiteCache.GetValue(name, Source);
        set => CallSiteCache.SetValue(name, Source, value);
    }

    public override bool TryGetValue(string name, out object value)
    {
        throw new NotImplementedException();
    }

    public override bool TrySetValue(string name, object value)
    {
        throw new NotImplementedException();
    }
}
