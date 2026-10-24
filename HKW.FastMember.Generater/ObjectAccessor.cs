using HKW.FastMember.Abstractions;

namespace HKW.FastMember.Generater;

/// <summary>
/// 为静态已知的具体模型类型创建绑定访问器
/// </summary>
public static class ObjectAccessor
{
    /// <summary>
    /// 将非空源对象绑定到为其静态类型生成的访问器
    /// </summary>
    /// <exception cref="ArgumentNullException">源对象为空</exception>
    /// <exception cref="InvalidOperationException">具体类型尚未注册生成的访问器</exception>
    public static ObjectAccessor<T> Create<T>(T source)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(source);
        return new(source, GeneratedAccessorRegistry<T>.GetAccessor());
    }
}
