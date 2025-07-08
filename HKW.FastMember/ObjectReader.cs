using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace HKW.FastMember;

/// <summary>
/// 提供一种将对象序列作为数据读取器进行读取的方法
/// 例如: 用于SqlBulkCopy或其他面向数据库的代码
/// </summary>
public abstract partial class ObjectReader : DbDataReader, IDbColumnSchemaGenerator
{
    /// <summary>
    /// 深度
    /// </summary>
    public override int Depth => 0;

    private readonly TypeAccessor _accessor;
    private readonly string[] _memberNames;
    private readonly Type[] _effectiveTypes;
    private readonly BitArray _allowNull;

    /// <summary>
    /// 当前对象
    /// </summary>
    protected object? _current;

    /// <summary>
    /// 创建一个新的ObjectReader实例，用于读取提供的数据
    /// </summary>
    /// <param name="source">要表示的对象序列</param>
    /// <param name="members">应该暴露给读取器的成员</param>
    public static ObjectReader Create<T>(IEnumerable<T> source, params string[] members) =>
        new SyncObjectReader(typeof(T), source, members);

    /// <summary>
    /// 创建一个新的ObjectReader实例，用于读取提供的数据
    /// </summary>
    /// <param name="type">要读取的信息的预期类型</param>
    /// <param name="members">应该暴露给读取器的成员</param>
    internal ObjectReader(Type type, params string[] members)
    {
        var isEmptyMembers = members is null || members.Length == 0;

        _accessor = TypeAccessor.Create(type);
        if (_accessor.GetMembersSupported is false && isEmptyMembers)
        {
            throw new InvalidOperationException(
                "Member information is not available for this type; the required members must be specified explicitly"
            );
        }
        // 按序号首先排序成员，然后按名称排序。
        var typeMembers = _accessor.GetMembers().OrderBy(p => p.Ordinal).ToList();

        if (isEmptyMembers)
        {
            members = new string[typeMembers.Count];
            for (int i = 0; i < members.Length; i++)
            {
                members[i] = typeMembers[i].Name;
            }
        }

        _allowNull = new BitArray(members!.Length);
        _effectiveTypes = new Type[members.Length];
        for (var i = 0; i < members.Length; i++)
        {
            Type? memberType = null;
            var allowNull = true;
            var hunt = members[i];
            foreach (var member in typeMembers)
            {
                if (member.Name == hunt)
                {
                    if (memberType == null)
                    {
                        var tmp = member.Type;
                        memberType = Nullable.GetUnderlyingType(tmp) ?? tmp;

                        allowNull = !(memberType.IsValueType && memberType == tmp);

                        // 继续检查, 防止重复
                    }
                    else
                    {
                        // 如果重复则忽视
                        memberType = null;
                        break;
                    }
                }
            }
            _allowNull[i] = allowNull;
            _effectiveTypes[i] = memberType ?? typeof(object);
        }

        _current = null;
        _memberNames = [.. members];
    }

#if !NETFRAMEWORK
    private ReadOnlyCollection<DbColumn>? _columnSchema;

    private ReadOnlyCollection<DbColumn> BuildColumnSchema()
    {
        var arr = new DbColumn[_memberNames.Length];
        for (int i = 0; i < _memberNames.Length; i++)
        {
            arr[i] = new ObjectReaderDbColumn(
                i,
                _memberNames[i],
                _effectiveTypes == null ? typeof(object) : _effectiveTypes[i],
                _allowNull == null || _allowNull[i]
            );
        }
        return _columnSchema = new ReadOnlyCollection<DbColumn>(arr);
    }

    private sealed class ObjectReaderDbColumn : DbColumn
    {
        internal ObjectReaderDbColumn(int ordinal, string name, Type type, bool allowNull)
        {
            ColumnOrdinal = ordinal;
            ColumnName = name;
            DataType = type;
            ColumnSize = -1;
            AllowDBNull = allowNull;
        }
    }

    ReadOnlyCollection<DbColumn> IDbColumnSchemaGenerator.GetColumnSchema() =>
        _columnSchema ?? BuildColumnSchema();
#endif

    /// <inheritdoc />
    public override DataTable GetSchemaTable()
    {
        // 这些是DataTable加载使用的列
        var table = new DataTable()
        {
            Columns =
            {
                { "ColumnOrdinal", typeof(int) },
                { "ColumnName", typeof(string) },
                { "DataType", typeof(Type) },
                { "ColumnSize", typeof(int) },
                { "AllowDBNull", typeof(bool) }
            }
        };
        var rowData = new object[5];
        for (var i = 0; i < _memberNames.Length; i++)
        {
            rowData[0] = i;
            rowData[1] = _memberNames[i];
            rowData[2] = _effectiveTypes == null ? typeof(object) : _effectiveTypes[i];
            rowData[3] = -1;
            rowData[4] = _allowNull == null || _allowNull[i];
            table.Rows.Add(rowData);
        }
        return table;
    }

    /// <inheritdoc/>
    public override void Close()
    {
        Shutdown();
    }

    /// <inheritdoc/>
    public override bool HasRows => active;
    private bool active = true;

    /// <inheritdoc/>
    public override bool NextResult()
    {
        active = false;
        return false;
    }

    /// <inheritdoc/>
    public override int RecordsAffected => 0;

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
            Shutdown();
    }

    /// <summary>
    /// 关闭读取器并释放资源
    /// </summary>
    protected virtual void Shutdown()
    {
        active = false;
        _current = null;
    }

    /// <inheritdoc/>
    public override int FieldCount => _memberNames.Length;

    /// <inheritdoc/>
    public override bool GetBoolean(int ordinal) => (bool)this[ordinal]!;

    /// <inheritdoc/>
    public override byte GetByte(int ordinal) => (byte)this[ordinal]!;

    /// <inheritdoc/>
    public override long GetBytes(
        int i,
        long fieldOffset,
        byte[]? buffer,
        int bufferoffset,
        int length
    )
    {
        byte[] s = (byte[])this[i]!;
        int available = s.Length - (int)fieldOffset;
        if (available <= 0)
            return 0;

        int count = Math.Min(length, available);
        Buffer.BlockCopy(s, (int)fieldOffset, buffer!, bufferoffset, count);
        return count;
    }

    /// <inheritdoc/>
    public override char GetChar(int ordinal) => (char)this[ordinal]!;

    /// <inheritdoc/>
    public override long GetChars(
        int i,
        long fieldoffset,
        char[]? buffer,
        int bufferoffset,
        int length
    )
    {
        string s = (string)this[i]!;
        int available = s.Length - (int)fieldoffset;
        if (available <= 0)
            return 0;

        int count = Math.Min(length, available);
        s.CopyTo((int)fieldoffset, buffer!, bufferoffset, count);
        return count;
    }

    /// <inheritdoc/>
    protected override DbDataReader GetDbDataReader(int i)
    {
        throw new NotSupportedException();
    }

    /// <inheritdoc/>
    public override string GetDataTypeName(int i)
    {
        return (_effectiveTypes == null ? typeof(object) : _effectiveTypes[i]).Name;
    }

    /// <inheritdoc/>
    public override DateTime GetDateTime(int i)
    {
        return (DateTime)this[i]!;
    }

    /// <inheritdoc/>
    public override decimal GetDecimal(int ordinal)
    {
        return (decimal)this[ordinal]!;
    }

    /// <inheritdoc/>
    public override double GetDouble(int ordinal)
    {
        return (double)this[ordinal]!;
    }

    /// <inheritdoc/>
    public override Type GetFieldType(int ordinal)
    {
        return _effectiveTypes == null ? typeof(object) : _effectiveTypes[ordinal];
    }

    /// <inheritdoc/>
    public override float GetFloat(int ordinal)
    {
        return (float)this[ordinal]!;
    }

    /// <inheritdoc/>
    public override Guid GetGuid(int ordinal)
    {
        return (Guid)this[ordinal]!;
    }

    /// <inheritdoc/>
    public override short GetInt16(int ordinal)
    {
        return (short)this[ordinal];
    }

    /// <inheritdoc/>
    public override int GetInt32(int ordinal)
    {
        return (int)this[ordinal]!;
    }

    /// <inheritdoc/>
    public override long GetInt64(int ordinal)
    {
        return (long)this[ordinal]!;
    }

    /// <inheritdoc/>
    public override string GetName(int ordinal)
    {
        return _memberNames[ordinal];
    }

    /// <inheritdoc/>
    public override int GetOrdinal(string name)
    {
        return Array.IndexOf(_memberNames, name);
    }

    /// <inheritdoc/>
    public override string GetString(int ordinal)
    {
        return (string)this[ordinal]!;
    }

    /// <inheritdoc/>
    public override object GetValue(int ordinal)
    {
        return this[ordinal]!;
    }

    /// <inheritdoc/>
    public override IEnumerator GetEnumerator() => new DbEnumerator(this);

    /// <inheritdoc/>
    public override int GetValues(object[] values)
    {
        // 在堆栈上复制关键字段
        var members = _memberNames;
        var current = _current;
        var accessor = _accessor;

        int count = Math.Min(values.Length, members.Length);
        for (int i = 0; i < count; i++)
            values[i] = accessor[current!, members[i]] ?? DBNull.Value;
        return count;
    }

    /// <inheritdoc/>
    public override bool IsDBNull(int ordinal)
    {
        return this[ordinal] is DBNull;
    }

    /// <summary>
    /// 获取指定成员中当前对象的值
    /// </summary>
    public override object this[string name] => _accessor[_current!, name] ?? DBNull.Value;

    /// <summary>
    /// 获取指定索引处的成员值
    /// </summary>
    /// <param name="i">索引</param>
    /// <returns>成员值</returns>
    public override object this[int i] => _accessor[_current!, _memberNames[i]] ?? DBNull.Value;

    private sealed class SyncObjectReader : ObjectReader
    {
        private IEnumerator? _source;

        internal SyncObjectReader(Type type, IEnumerable source, string[] members)
            : base(type, members)
        {
            if (source == null)
                throw new ArgumentOutOfRangeException(nameof(source));

            _source = source.GetEnumerator();
        }

        protected override void Shutdown()
        {
            base.Shutdown();
            var tmp = _source as IDisposable;
            _source = null;
            tmp?.Dispose();
        }

        public override bool IsClosed => _source == null;

        public override bool Read()
        {
            if (active)
            {
                var tmp = _source;
                if (tmp != null && tmp.MoveNext())
                {
                    _current = tmp.Current;
                    return true;
                }
                else
                {
                    active = false;
                }
            }
            _current = null;
            return false;
        }
    }
}
