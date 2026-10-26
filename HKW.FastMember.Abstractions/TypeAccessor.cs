using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Collections.ObjectModel;

namespace HKW.FastMember.Abstractions;

/// <summary>
/// 提供与后端无关的索引式成员访问, 并共享元数据和验证逻辑
/// </summary>
public abstract class TypeAccessor
{
    private readonly ImmutableArray<Member> _members;
    private readonly FrozenDictionary<string, Member> _memberDictionary;
    private readonly FrozenDictionary<string, int> _indices;

    /// <summary>
    /// 按名称的序号比较顺序生成描述符快照, 名称必须唯一, 核心索引遵循该顺序
    /// </summary>
    protected TypeAccessor(
        Type targetType,
        IEnumerable<Member> members,
        bool createNewSupported,
        int contractVersion = 1,
        bool getMembersSupported = true
    )
    {
        ArgumentNullException.ThrowIfNull(targetType);
        ArgumentNullException.ThrowIfNull(members);
        var snapshot = members.ToArray();
        var indices = new Dictionary<string, int>(StringComparer.Ordinal);
        var dictionary = new Dictionary<string, Member>(StringComparer.Ordinal);
        for (var index = 0; index < snapshot.Length; index++)
        {
            var member = snapshot[index];
            ArgumentNullException.ThrowIfNull(member);
            if (
                index > 0
                && StringComparer.Ordinal.Compare(snapshot[index - 1].Name, member.Name) >= 0
            )
                throw new ArgumentException(
                    "Members must have unique names in StringComparer.Ordinal order.",
                    nameof(members)
                );
            dictionary.Add(member.Name, member);
            indices.Add(member.Name, index);
        }
        _indices = indices.ToFrozenDictionary();
        _members = snapshot.ToImmutableArray();
        _memberDictionary = dictionary.ToFrozenDictionary();
        TargetType = targetType;
        CreateNewSupported = createNewSupported;
        ContractVersion = contractVersion;
        GetMembersSupported = getMembersSupported;
    }

    public Type TargetType { get; }
    public bool GetMembersSupported { get; }
    public int ContractVersion { get; }
    public bool CreateNewSupported { get; }

    /// <summary>
    /// 返回不可变的静态元数据, 若此访问器没有固定成员表, 则抛出异常
    /// </summary>
    public IReadOnlyList<Member> GetMembers() =>
        GetMembersSupported
            ? _members
            : throw new NotSupportedException(
                "This accessor does not expose static member metadata."
            );

    /// <summary>
    /// 返回以成员名称为键的不可变元数据, 使用区分大小写的序号比较
    /// </summary>
    public IReadOnlyDictionary<string, Member> GetMemberDictionary() =>
        GetMembersSupported
            ? _memberDictionary
            : throw new NotSupportedException(
                "This accessor does not expose static member metadata."
            );

    /// <summary>
    /// 读取或写入指定名称的成员。动态后端可重写基于名称的访问。
    /// </summary>
    public virtual object? this[object target, string name]
    {
        get
        {
            if (TryGetValue(target, name, out var value))
                return value;
            throw new ArgumentOutOfRangeException(
                nameof(name),
                name,
                "Member does not exist or is not readable."
            );
        }
        set
        {
            if (TrySetValue(target, name, value) is false)
                throw new ArgumentOutOfRangeException(
                    nameof(name),
                    name,
                    "Member does not exist or is not writable."
                );
        }
    }

    /// <summary>
    /// 仅在成员未知或不可读时返回 false, 用户取值器异常会向上传播
    /// </summary>
    public virtual bool TryGetValue(object target, string name, out object? value)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(name);
        if (_indices.TryGetValue(name, out var index) && _members[index].CanRead)
        {
            value = GetValueCore(target, index);
            return true;
        }
        value = null;
        return false;
    }

    /// <summary>
    /// 仅在成员未知或不可写时返回 false, 转换异常和用户设值器异常会向上传播
    /// </summary>
    public virtual bool TrySetValue(object target, string name, object? value)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(name);
        if (_indices.TryGetValue(name, out var index) is false || _members[index].CanWrite is false)
            return false;
        SetValueCore(target, index, value);
        return true;
    }

    public object CreateNew() =>
        CreateNewSupported
            ? CreateNewCore()
            : throw new NotSupportedException(
                $"Type '{TargetType}' has no supported public parameterless constructor."
            );

    /// <summary>
    /// 读取已验证的成员索引, 仅基于名称的后端无需实现索引式访问
    /// </summary>
    protected virtual object? GetValueCore(object target, int index) =>
        throw new NotSupportedException();

    /// <summary>
    /// 写入已验证的成员索引, 不捕获用户代码异常或转换异常
    /// </summary>
    protected virtual void SetValueCore(object target, int index, object? value) =>
        throw new NotSupportedException();

    /// <summary>
    /// 仅在支持构造时创建目标实例
    /// </summary>
    protected virtual object CreateNewCore() => throw new NotSupportedException();
}
