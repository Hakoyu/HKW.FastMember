using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

namespace HKW.FastMember;

/// <summary>
/// 提供一种将对象序列作为数据读取器进行读取的方法
/// 例如：用于SqlBulkCopy或其他面向数据库的代码
/// </summary>
public abstract partial class ObjectReader : DbDataReader
{
    static readonly Task<bool> _trueTaskResult = Task.FromResult(true);
    static readonly Task<bool> _falseTaskResult = Task.FromResult(false);

    private sealed class AsyncObjectReader<T> : ObjectReader, IAsyncDisposable
    {
        private IAsyncEnumerator<T>? _source;

        internal AsyncObjectReader(
            Type type,
            IAsyncEnumerable<T> source,
            string[] members,
            CancellationToken cancellationToken
        )
            : base(type, members)
        {
            if (source == null)
                throw new ArgumentOutOfRangeException(nameof(source));

            cancellationToken.ThrowIfCancellationRequested();
            _source = source.GetAsyncEnumerator(cancellationToken);
        }

        ValueTask IAsyncDisposable.DisposeAsync()
        {
            base.Shutdown();
            var tmp = _source;
            return tmp?.DisposeAsync() ?? default;
        }

        protected override void Shutdown()
        {
            base.Shutdown();
            var tmp = _source;
            _source = null!;
            tmp?.DisposeAsync().AsTask().Wait();
        }

        public override bool IsClosed => _source == null;

        public override Task<bool> ReadAsync(CancellationToken cancellationToken)
        {
            static async Task<bool> FromAsync(AsyncObjectReader<T> reader, ValueTask<bool> pending)
            {
                if (await pending.ConfigureAwait(false))
                {
                    reader._current = reader._source!.Current;
                    return true;
                }
                reader.active = false;
                reader._current = null;
                return false;
            }
            if (active)
            {
                var tmp = _source;
                if (tmp != null)
                {
                    var pending = tmp.MoveNextAsync();
                    if (!pending.IsCompletedSuccessfully)
                    {
                        return FromAsync(this, pending);
                    }
                    if (pending.Result)
                    {
                        _current = tmp.Current;
                        return _trueTaskResult;
                    }
                    else
                    {
                        active = false;
                    }
                }
                else
                {
                    active = false;
                }
            }
            _current = null;
            return _falseTaskResult;
        }
    }

    /// <inheritdoc/>
    public override bool Read() => ReadAsync().GetAwaiter().GetResult();

    /// <summary>
    /// 创建一个新的ObjectReader实例，用于读取提供的数据
    /// </summary>
    /// <param name="source">要表示的对象序列</param>
    /// <param name="members">应该暴露给读取器的成员</param>
    /// <returns>对象读取器</returns>
    public static ObjectReader Create<T>(IAsyncEnumerable<T> source, params string[] members) =>
        new AsyncObjectReader<T>(typeof(T), source, members, CancellationToken.None);

    /// <summary>
    /// 创建一个新的ObjectReader实例，用于读取提供的数据
    /// </summary>
    /// <param name="source">要表示的对象序列</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <param name="members">应该暴露给读取器的成员</param>
    /// <returns>对象读取器</returns>
    public static ObjectReader Create<T>(
        IAsyncEnumerable<T> source,
        CancellationToken cancellationToken,
        params string[] members
    ) => new AsyncObjectReader<T>(typeof(T), source, members, cancellationToken);
}
