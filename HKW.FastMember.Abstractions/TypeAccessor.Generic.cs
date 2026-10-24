namespace HKW.FastMember.Abstractions;

/// <summary>
/// 使用静态 <typeparamref name="T"/> 契约和通用的名称到索引查找提供强类型访问
/// </summary>
public abstract class TypeAccessor<T> : TypeAccessor
    where T : class
{
    protected TypeAccessor(
        IEnumerable<Member> members,
        bool createNewSupported,
        int contractVersion = 1
    )
        : this(members, createNewSupported, contractVersion, true) { }

    /// <summary>
    /// 允许强类型的基于名称的后端声明不提供固定成员元数据
    /// </summary>
    protected TypeAccessor(
        IEnumerable<Member> members,
        bool createNewSupported,
        int contractVersion,
        bool getMembersSupported
    )
        : base(typeof(T), members, createNewSupported, contractVersion, getMembersSupported) { }

    public object? this[T target, string name]
    {
        get => ((TypeAccessor)this)[target, name];
        set => ((TypeAccessor)this)[target, name] = value;
    }

    public bool TryGetValue(T target, string name, out object? value) =>
        ((TypeAccessor)this).TryGetValue(target, name, out value);

    public bool TrySetValue(T target, string name, object? value) =>
        ((TypeAccessor)this).TrySetValue(target, name, value);

    public new T CreateNew() => (T)base.CreateNew();

    protected sealed override object? GetValueCore(object target, int index) =>
        GetValueTypedCore((T)target, index);

    protected sealed override void SetValueCore(object target, int index, object? value) =>
        SetValueTypedCore((T)target, index, value);

    protected sealed override object CreateNewCore() => CreateNewTypedCore();

    /// <summary>
    /// 直接读取描述符的索引, 公共入口已验证名称和权限
    /// </summary>
    protected abstract object? GetValueTypedCore(T target, int index);

    /// <summary>
    /// 直接写入描述符的索引, 转换异常和用户代码异常必须向上传播
    /// </summary>
    protected abstract void SetValueTypedCore(T target, int index, object? value);

    /// <summary>
    /// 仅在支持构造时重写
    /// </summary>
    protected virtual T CreateNewTypedCore() => throw new NotSupportedException();
}
