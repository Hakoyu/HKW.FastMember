﻿using System;
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
/// Provides a means of reading a sequence of objects as a data-reader, for example
/// for use with SqlBulkCopy or other data-base oriented code
/// </summary>
public abstract partial class ObjectReader : DbDataReader, IDbColumnSchemaGenerator
{
    public override int Depth => 0;

    private readonly TypeAccessor _accessor;
    private readonly string[] _memberNames;
    private readonly Type[] _effectiveTypes;
    private readonly BitArray _allowNull;

    protected object _current;

    private sealed class SyncObjectReader : ObjectReader
    {
        private IEnumerator source;

        internal SyncObjectReader(Type type, IEnumerable source, string[] members)
            : base(type, members)
        {
            if (source == null)
                throw new ArgumentOutOfRangeException(nameof(source));

            this.source = source.GetEnumerator();
        }

        protected override void Shutdown()
        {
            base.Shutdown();
            var tmp = source as IDisposable;
            source = null;
            if (tmp != null)
                tmp.Dispose();
        }

        public override bool IsClosed => source == null;

        public override bool Read()
        {
            if (active)
            {
                var tmp = source;
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

    /// <summary>
    /// Creates a new ObjectReader instance for reading the supplied data
    /// </summary>
    /// <param name="source">The sequence of objects to represent</param>
    /// <param name="members">The members that should be exposed to the reader</param>
    public static ObjectReader Create<T>(IEnumerable<T> source, params string[] members) =>
        new SyncObjectReader(typeof(T), source, members);

    /// <summary>
    /// Creates a new ObjectReader instance for reading the supplied data
    /// </summary>
    /// <param name="type">The expected Type of the information to be read</param>
    /// <param name="source">The sequence of objects to represent</param>
    /// <param name="members">The members that should be exposed to the reader</param>
    internal ObjectReader(Type type, params string[] members)
    {
        bool allMembers = members == null || members.Length == 0;

        this._accessor = TypeAccessor.Create(type);
        if (_accessor.GetMembersSupported)
        {
            // Sort members by ordinal first and then by name.
            var typeMembers = this._accessor.GetMembers().OrderBy(p => p.Ordinal).ToList();

            if (allMembers)
            {
                members = new string[typeMembers.Count];
                for (int i = 0; i < members.Length; i++)
                {
                    members[i] = typeMembers[i].Name;
                }
            }

            this._allowNull = new BitArray(members.Length);
            this._effectiveTypes = new Type[members.Length];
            for (int i = 0; i < members.Length; i++)
            {
                Type memberType = null;
                bool allowNull = true;
                string hunt = members[i];
                foreach (var member in typeMembers)
                {
                    if (member.Name == hunt)
                    {
                        if (memberType == null)
                        {
                            var tmp = member.Type;
                            memberType = Nullable.GetUnderlyingType(tmp) ?? tmp;

                            allowNull = !(memberType.IsValueType && memberType == tmp);

                            // but keep checking, in case of duplicates
                        }
                        else
                        {
                            memberType = null; // duplicate found; say nothing
                            break;
                        }
                    }
                }
                this._allowNull[i] = allowNull;
                this._effectiveTypes[i] = memberType ?? typeof(object);
            }
        }
        else if (allMembers)
        {
            throw new InvalidOperationException(
                "Member information is not available for this type; the required members must be specified explicitly"
            );
        }

        this._current = null;
        this._memberNames = (string[])members.Clone();
    }

#if !NETFRAMEWORK
    private ReadOnlyCollection<DbColumn> _columnSchema;

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

    public override DataTable GetSchemaTable()
    {
        // these are the columns used by DataTable load
        DataTable table =
            new()
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
        object[] rowData = new object[5];
        for (int i = 0; i < _memberNames.Length; i++)
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

    public override void Close()
    {
        Shutdown();
    }

    public override bool HasRows
    {
        get { return active; }
    }
    private bool active = true;

    public override bool NextResult()
    {
        active = false;
        return false;
    }

    public override int RecordsAffected
    {
        get { return 0; }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
            Shutdown();
    }

    protected virtual void Shutdown()
    {
        active = false;
        _current = null;
    }

    public override int FieldCount
    {
        get { return _memberNames.Length; }
    }

    public override bool GetBoolean(int i)
    {
        return (bool)this[i];
    }

    public override byte GetByte(int i)
    {
        return (byte)this[i];
    }

    public override long GetBytes(
        int i,
        long fieldOffset,
        byte[] buffer,
        int bufferoffset,
        int length
    )
    {
        byte[] s = (byte[])this[i];
        int available = s.Length - (int)fieldOffset;
        if (available <= 0)
            return 0;

        int count = Math.Min(length, available);
        Buffer.BlockCopy(s, (int)fieldOffset, buffer, bufferoffset, count);
        return count;
    }

    public override char GetChar(int i)
    {
        return (char)this[i];
    }

    public override long GetChars(
        int i,
        long fieldoffset,
        char[] buffer,
        int bufferoffset,
        int length
    )
    {
        string s = (string)this[i];
        int available = s.Length - (int)fieldoffset;
        if (available <= 0)
            return 0;

        int count = Math.Min(length, available);
        s.CopyTo((int)fieldoffset, buffer, bufferoffset, count);
        return count;
    }

    protected override DbDataReader GetDbDataReader(int i)
    {
        throw new NotSupportedException();
    }

    public override string GetDataTypeName(int i)
    {
        return (_effectiveTypes == null ? typeof(object) : _effectiveTypes[i]).Name;
    }

    public override DateTime GetDateTime(int i)
    {
        return (DateTime)this[i];
    }

    public override decimal GetDecimal(int i)
    {
        return (decimal)this[i];
    }

    public override double GetDouble(int i)
    {
        return (double)this[i];
    }

    public override Type GetFieldType(int i)
    {
        return _effectiveTypes == null ? typeof(object) : _effectiveTypes[i];
    }

    public override float GetFloat(int i)
    {
        return (float)this[i];
    }

    public override Guid GetGuid(int i)
    {
        return (Guid)this[i];
    }

    public override short GetInt16(int i)
    {
        return (short)this[i];
    }

    public override int GetInt32(int i)
    {
        return (int)this[i];
    }

    public override long GetInt64(int i)
    {
        return (long)this[i];
    }

    public override string GetName(int i)
    {
        return _memberNames[i];
    }

    public override int GetOrdinal(string name)
    {
        return Array.IndexOf(_memberNames, name);
    }

    public override string GetString(int i)
    {
        return (string)this[i];
    }

    public override object GetValue(int i)
    {
        return this[i];
    }

    public override IEnumerator GetEnumerator() => new DbEnumerator(this);

    public override int GetValues(object[] values)
    {
        // duplicate the key fields on the stack
        var members = this._memberNames;
        var current = this._current;
        var accessor = this._accessor;

        int count = Math.Min(values.Length, members.Length);
        for (int i = 0; i < count; i++)
            values[i] = accessor[current, members[i]] ?? DBNull.Value;
        return count;
    }

    public override bool IsDBNull(int i)
    {
        return this[i] is DBNull;
    }

    public override object this[string name]
    {
        get { return _accessor[_current, name] ?? DBNull.Value; }
    }

    /// <summary>
    /// Gets the value of the current object in the member specified
    /// </summary>
    public override object this[int i]
    {
        get { return _accessor[_current, _memberNames[i]] ?? DBNull.Value; }
    }
}
