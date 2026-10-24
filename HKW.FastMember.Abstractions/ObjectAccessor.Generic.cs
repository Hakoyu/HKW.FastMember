namespace HKW.FastMember.Abstractions;

/// <summary>
/// 使用源的静态 <typeparamref name="T"/> 契约绑定源, 同时复用通用对象包装器
/// </summary>
public sealed class ObjectAccessor<T> : ObjectAccessor
    where T : class
{
    public ObjectAccessor(T source, TypeAccessor<T> accessor)
        : base(source, accessor) { }

    public new T Source => (T)base.Source;
}
