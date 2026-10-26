namespace HKW.FastMember.Abstractions;

/// <summary>
/// 将不可变的源引用绑定到显式提供的访问器, 不执行后端发现
/// </summary>
public class ObjectAccessor
{
    private readonly TypeAccessor _accessor;

    public ObjectAccessor(object source, TypeAccessor accessor)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(accessor);
        if (accessor.TargetType.IsInstanceOfType(source) is false)
            throw new ArgumentException(
                "The source must be compatible with the accessor's target type.",
                nameof(source)
            );
        Source = source;
        _accessor = accessor;
    }

    public object Source { get; }
    public object? this[string name]
    {
        get => _accessor[Source, name];
        set => _accessor[Source, name] = value;
    }

    public bool TryGetValue(string name, out object? value) =>
        _accessor.TryGetValue(Source, name, out value);

    public bool TrySetValue(string name, object? value) =>
        _accessor.TrySetValue(Source, name, value);

    public IReadOnlyList<Member> GetMembers() => _accessor.GetMembers();

    public IReadOnlyDictionary<string, Member> GetMemberDictionary() =>
        _accessor.GetMemberDictionary();

    public override bool Equals(object? obj) => Source.Equals(obj);

    public override int GetHashCode() => Source.GetHashCode();

    public override string? ToString() => Source.ToString();
}
