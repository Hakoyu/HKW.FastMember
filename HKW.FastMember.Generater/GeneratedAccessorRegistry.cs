using System.ComponentModel;
using System.Threading;
using HKW.FastMember.Abstractions;

namespace HKW.FastMember.Generater;

/// <summary>
/// 为生成的模块初始化器提供基础设施, 不用于运行时发现, 也不构成安全边界
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class GeneratedAccessorRegistry<T>
    where T : class
{
    private static TypeAccessor<T>? _accessor;

    /// <summary>
    /// 以原子方式发布访问器, 不同使用方程序集可幂等注册等价契约
    /// </summary>
    public static void Register(TypeAccessor<T> accessor)
    {
        ArgumentNullException.ThrowIfNull(accessor);
        var existing = Interlocked.CompareExchange(ref _accessor, accessor, null);
        if (
            existing is not null
            && !GeneratedAccessorContractComparer.HasSameContract(existing, accessor)
        )
            throw new InvalidOperationException(
                $"Conflicting generated accessor contracts for type '{typeof(T)}'. Ensure all consumers use the same generator contract and model."
            );
    }

    /// <summary>
    /// 获取生成的单例, 不使用反射或动态代码作为后备
    /// </summary>
    public static TypeAccessor<T> GetAccessor() =>
        Volatile.Read(ref _accessor)
        ?? throw new InvalidOperationException(
            $"No generated accessor is registered for type '{typeof(T)}'. Ensure a direct concrete-type Create call is used and the source generator is enabled. Access from custom module initializers may precede registration."
        );
}
