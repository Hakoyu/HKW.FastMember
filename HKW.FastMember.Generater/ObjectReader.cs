using CommonReader = HKW.FastMember.Abstractions.ObjectReader;

namespace HKW.FastMember.Generater;

/// <summary>
/// 仅使用已注册的静态生成访问器创建公共读取器
/// </summary>
public static class ObjectReader
{
    public static CommonReader Create<T>(IEnumerable<T> source, params string[] members)
        where T : class
    {
        ValidateArguments(source, members);
        return CommonReader.Create(source, GeneratedAccessorRegistry<T>.GetAccessor(), members);
    }

    public static CommonReader Create<T>(IAsyncEnumerable<T> source, params string[] members)
        where T : class => Create(source, CancellationToken.None, members);

    /// <summary>
    /// 首次获取异步枚举器时, 原样传入创建时指定的取消令牌
    /// </summary>
    public static CommonReader Create<T>(
        IAsyncEnumerable<T> source,
        CancellationToken cancellationToken,
        params string[] members
    )
        where T : class
    {
        ValidateArguments(source, members);
        cancellationToken.ThrowIfCancellationRequested();
        return CommonReader.Create(
            source,
            GeneratedAccessorRegistry<T>.GetAccessor(),
            cancellationToken,
            members
        );
    }

    private static void ValidateArguments(object source, string[] members)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(members);
        foreach (var name in members)
            ArgumentNullException.ThrowIfNull(name);
    }
}
