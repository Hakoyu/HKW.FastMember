using HKW.FastMember.Abstractions;

namespace HKW.FastMember.Generater;

/// <summary>
/// 为静态已知的具体模型类型提供生成访问器的入口
/// </summary>
public static class TypeAccessor
{
    /// <summary>
    /// 返回为编译期具体类型 <typeparamref name="T"/> 生成的访问器
    /// </summary>
    /// <exception cref="InvalidOperationException">生成已禁用或尚未完成注册</exception>
    public static TypeAccessor<T> Create<T>()
        where T : class => GeneratedAccessorRegistry<T>.GetAccessor();
}
