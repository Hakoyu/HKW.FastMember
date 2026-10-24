using System.Collections;
using System.Collections.ObjectModel;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;

namespace HKW.FastMember.Abstractions;

/// <summary>
/// 基于注入的成员访问器提供单结果集读取器, 获取元数据绝不会枚举源
/// </summary>
/// <remarks>
/// 对于异步源, 优先使用 ReadAsync/DisposeAsync
/// 同步操作可能因用户代码而阻塞 ConfigureAwait(false) 无法阻止用户枚举器内部捕获上下文
/// 并发读取会被拒绝, 不支持并发执行读取和释放操作
/// </remarks>
public abstract partial class ObjectReader : DbDataReader, IDbColumnSchemaGenerator
{
    public static ObjectReader Create<T>(
        IEnumerable<T> source,
        TypeAccessor accessor,
        params string[] members
    )
    {
        ValidateArguments(source, members);
        ValidateAccessor<T>(accessor);
        return new SyncReader<T>(source, accessor, members);
    }

    public static ObjectReader Create<T>(
        IAsyncEnumerable<T> source,
        TypeAccessor accessor,
        params string[] members
    ) => Create(source, accessor, CancellationToken.None, members);

    /// <summary>
    /// 首次获取异步枚举器时, 原样传递创建时的令牌
    /// </summary>
    public static ObjectReader Create<T>(
        IAsyncEnumerable<T> source,
        TypeAccessor accessor,
        CancellationToken cancellationToken,
        params string[] members
    )
    {
        ValidateArguments(source, members);
        ValidateAccessor<T>(accessor);
        cancellationToken.ThrowIfCancellationRequested();
        return new AsyncReader<T>(source, accessor, cancellationToken, members);
    }

    private static void ValidateAccessor<T>(TypeAccessor accessor)
    {
        ArgumentNullException.ThrowIfNull(accessor);
        if (accessor.TargetType.IsAssignableFrom(typeof(T)) is false)
            throw new ArgumentException(
                "The accessor must accept the source's static element type.",
                nameof(accessor)
            );
        if (accessor.GetMembersSupported is false)
            throw new NotSupportedException(
                "A reader requires fixed member metadata. Supply a backend-specific column adapter for dynamic objects."
            );
    }

    private static void ValidateArguments(object source, string[] members)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(members);
        foreach (var name in members)
            ArgumentNullException.ThrowIfNull(name);
    }

    public abstract ReadOnlyCollection<DbColumn> GetColumnSchema();

    private abstract class Reader<T> : ObjectReader
    {
        private readonly TypeAccessor _accessor;
        private readonly Member[] _columns;
        private bool _closed,
            _ended,
            _prefetched,
            _onRow;
        private bool? _hasRows;
        private int _reading;
        private T? _current;

        protected Reader(TypeAccessor accessor, string[] members)
        {
            _accessor = accessor;
            if (members.Length == 0)
                _columns = _accessor
                    .GetMembers()
                    .Where(m => m.CanRead)
                    .OrderBy(m => m.Ordinal)
                    .ThenBy(m => m.Name, StringComparer.Ordinal)
                    .ToArray();
            else
            {
                var map = _accessor.GetMemberDictionary();
                _columns = new Member[members.Length];
                for (var i = 0; i < members.Length; i++)
                {
                    if (
                        map.TryGetValue(members[i], out var member) is false
                        || member.CanRead is false
                    )
                        throw new ArgumentOutOfRangeException(
                            nameof(members),
                            members[i],
                            "Member does not exist or is not readable."
                        );
                    _columns[i] = member;
                }
            }
        }

        protected abstract ValueTask<bool> MoveNextAsync();
        protected abstract T Current { get; }
        protected abstract ValueTask DisposeSourceAsync();

        private void EnsureOpen()
        {
            if (_closed)
                throw new InvalidOperationException("The reader is closed.");
        }

        private void EnterRead()
        {
            EnsureOpen();
            if (Interlocked.CompareExchange(ref _reading, 1, 0) != 0)
                throw new InvalidOperationException(
                    "Overlapping reader operations are not supported."
                );
        }

        private void ClearRow()
        {
            _current = default;
            _onRow = _prefetched = false;
        }

        private Member Column(int ordinal)
        {
            EnsureOpen();
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(
                (uint)ordinal,
                (uint)_columns.Length
            );
            return _columns[ordinal];
        }

        private async ValueTask<bool> AdvanceAsync()
        {
            if (_ended)
                return false;
            try
            {
                if (await MoveNextAsync().ConfigureAwait(false))
                {
                    _current = Current;
                    if (_current is null)
                        throw new InvalidOperationException("The source returned a null row.");
                    _hasRows = true;
                    return true;
                }
                _ended = true;
                _hasRows ??= false;
                ClearRow();
                await DisposeSourceAsync().ConfigureAwait(false);
                return false;
            }
            catch
            {
                _ended = true;
                _hasRows ??= false;
                ClearRow();
                // 清理操作不得替换原始的枚举异常（或 Current 异常）
                try
                {
                    await DisposeSourceAsync().ConfigureAwait(false);
                }
                catch { }
                throw;
            }
        }

        private async ValueTask<bool> ReadCoreAsync(CancellationToken cancellationToken)
        {
            EnsureOpen();
            cancellationToken.ThrowIfCancellationRequested();
            EnterRead();
            try
            {
                if (_prefetched)
                {
                    _prefetched = false;
                    _onRow = true;
                    return true;
                }
                ClearRow();
                _onRow = await AdvanceAsync().ConfigureAwait(false);
                return _onRow;
            }
            finally
            {
                Volatile.Write(ref _reading, 0);
            }
        }

        public override bool Read() =>
            ReadCoreAsync(CancellationToken.None).AsTask().GetAwaiter().GetResult();

        /// <summary>
        /// 在使用缓存行或向前移动之前检查调用令牌
        /// 它不会替换枚举器创建时的令牌, 也不会放弃已经运行的 <see cref="MoveNextAsync"/>
        /// </summary>
        public override Task<bool> ReadAsync(CancellationToken cancellationToken) =>
            ReadCoreAsync(cancellationToken).AsTask();

        public override bool HasRows
        {
            get
            {
                EnterRead();
                try
                {
                    if (_hasRows.HasValue is false)
                        _prefetched = AdvanceAsync().AsTask().GetAwaiter().GetResult();
                    return _hasRows == true;
                }
                finally
                {
                    Volatile.Write(ref _reading, 0);
                }
            }
        }

        public override bool NextResult() =>
            NextResultAsync(CancellationToken.None).GetAwaiter().GetResult();

        public override async Task<bool> NextResultAsync(CancellationToken cancellationToken)
        {
            EnsureOpen();
            cancellationToken.ThrowIfCancellationRequested();
            EnterRead();
            try
            {
                _ended = true;
                _hasRows = false;
                ClearRow();
                await DisposeSourceAsync().ConfigureAwait(false);
                return false;
            }
            finally
            {
                Volatile.Write(ref _reading, 0);
            }
        }

        public override void Close() => DisposeAsync().AsTask().GetAwaiter().GetResult();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                Close();
            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            if (_closed)
                return;
            _closed = _ended = true;
            ClearRow();
            await DisposeSourceAsync().ConfigureAwait(false);
            GC.SuppressFinalize(this);
        }

        public override bool IsClosed => _closed;
        public override int Depth => 0;
        public override int RecordsAffected => 0;
        public override int FieldCount
        {
            get
            {
                EnsureOpen();
                return _columns.Length;
            }
        }
        public override object this[int ordinal] => GetValue(ordinal);
        public override object this[string name] => GetValue(GetOrdinal(name));

        public override string GetName(int ordinal) => Column(ordinal).Name;

        [return: DynamicallyAccessedMembers(
            DynamicallyAccessedMemberTypes.PublicFields
                | DynamicallyAccessedMemberTypes.PublicProperties
        )]
        public override Type GetFieldType(int ordinal) => Column(ordinal).EffectiveType;

        public override string GetDataTypeName(int ordinal) => GetFieldType(ordinal).Name;

        public override int GetOrdinal(string name)
        {
            EnsureOpen();
            ArgumentNullException.ThrowIfNull(name);
            for (var i = 0; i < _columns.Length; i++)
                if (StringComparer.Ordinal.Equals(name, _columns[i].Name))
                    return i;
            throw new ArgumentOutOfRangeException($"Unknown column '{name}'.");
        }

        public override object GetValue(int ordinal)
        {
            var column = Column(ordinal);
            if (_onRow is false)
                throw new InvalidOperationException("The reader is not positioned on a row.");
            return _accessor[_current!, column.Name] ?? DBNull.Value;
        }

        public override int GetValues(object[] values)
        {
            EnsureOpen();
            ArgumentNullException.ThrowIfNull(values);
            if (_onRow is false)
                throw new InvalidOperationException("The reader is not positioned on a row.");
            var count = Math.Min(values.Length, _columns.Length);
            for (var i = 0; i < count; i++)
                values[i] = GetValue(i);
            return count;
        }

        public override bool IsDBNull(int ordinal) => GetValue(ordinal) is DBNull;

        public override TField GetFieldValue<TField>(int ordinal) => (TField)GetValue(ordinal);

        public override bool GetBoolean(int ordinal) => GetFieldValue<bool>(ordinal);

        public override byte GetByte(int ordinal) => GetFieldValue<byte>(ordinal);

        public override char GetChar(int ordinal) => GetFieldValue<char>(ordinal);

        public override DateTime GetDateTime(int ordinal) => GetFieldValue<DateTime>(ordinal);

        public override decimal GetDecimal(int ordinal) => GetFieldValue<decimal>(ordinal);

        public override double GetDouble(int ordinal) => GetFieldValue<double>(ordinal);

        public override float GetFloat(int ordinal) => GetFieldValue<float>(ordinal);

        public override Guid GetGuid(int ordinal) => GetFieldValue<Guid>(ordinal);

        public override short GetInt16(int ordinal) => GetFieldValue<short>(ordinal);

        public override int GetInt32(int ordinal) => GetFieldValue<int>(ordinal);

        public override long GetInt64(int ordinal) => GetFieldValue<long>(ordinal);

        public override string GetString(int ordinal) => GetFieldValue<string>(ordinal);

        public override long GetBytes(
            int ordinal,
            long dataOffset,
            byte[]? buffer,
            int bufferOffset,
            int length
        )
        {
            var data = GetFieldValue<byte[]>(ordinal);
            var count = CopyLength(data.Length, dataOffset, buffer, bufferOffset, length);
            if (buffer is null)
                return data.Length;
            if (count != 0)
                Array.Copy(data, (int)dataOffset, buffer, bufferOffset, count);
            return count;
        }

        public override long GetChars(
            int ordinal,
            long dataOffset,
            char[]? buffer,
            int bufferOffset,
            int length
        )
        {
            var value = GetValue(ordinal);
            var size = value switch
            {
                string text => text.Length,
                char[] chars => chars.Length,
                _ => throw new InvalidCastException(),
            };
            var count = CopyLength(size, dataOffset, buffer, bufferOffset, length);
            if (buffer is null)
                return size;
            if (count != 0)
            {
                if (value is string text)
                    text.CopyTo((int)dataOffset, buffer, bufferOffset, count);
                else
                    Array.Copy((char[])value, (int)dataOffset, buffer, bufferOffset, count);
            }
            return count;
        }

        private static int CopyLength(
            int size,
            long dataOffset,
            Array? buffer,
            int bufferOffset,
            int length
        )
        {
            ArgumentOutOfRangeException.ThrowIfNegative(dataOffset);
            ArgumentOutOfRangeException.ThrowIfNegative(bufferOffset);
            ArgumentOutOfRangeException.ThrowIfNegative(length);
            if (
                buffer is not null
                && (bufferOffset > buffer.Length || length > buffer.Length - bufferOffset)
            )
                throw new ArgumentException(
                    "The requested range exceeds the destination buffer.",
                    nameof(buffer)
                );
            return dataOffset >= size ? 0 : (int)Math.Min(length, size - dataOffset);
        }

        public override IEnumerator GetEnumerator()
        {
            EnsureOpen();
            return new DbEnumerator(this, false);
        }

        public override ReadOnlyCollection<DbColumn> GetColumnSchema()
        {
            EnsureOpen();
            return Array.AsReadOnly<DbColumn>(
                _columns.Select((m, i) => new ReaderColumn(m, i)).ToArray()
            );
        }

        public override DataTable GetSchemaTable()
        {
            EnsureOpen();
            var table = new DataTable("SchemaTable");
            table.Columns.Add(SchemaTableColumn.ColumnName, typeof(string));
            table.Columns.Add(SchemaTableColumn.ColumnOrdinal, typeof(int));
            table.Columns.Add(SchemaTableColumn.DataType, typeof(Type));
            table.Columns.Add(SchemaTableColumn.ColumnSize, typeof(int));
            table.Columns.Add(SchemaTableColumn.AllowDBNull, typeof(bool));
            table.Columns.Add(SchemaTableColumn.IsKey, typeof(bool));
            for (var i = 0; i < _columns.Length; i++)
            {
                var m = _columns[i];
                table.Rows.Add(m.Name, i, m.EffectiveType, -1, m.AllowDBNull, false);
            }
            return table;
        }
    }

    private sealed class ReaderColumn : DbColumn
    {
        public ReaderColumn(Member member, int ordinal)
        {
            ColumnName = member.Name;
            ColumnOrdinal = ordinal;
            DataType = member.EffectiveType;
            DataTypeName = member.EffectiveType.Name;
            ColumnSize = -1;
            AllowDBNull = member.AllowDBNull;
            IsKey = false;
        }
    }
}
