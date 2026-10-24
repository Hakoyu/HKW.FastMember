namespace HKW.FastMember.Abstractions;

/// <summary>
/// 指定读取器的排序, 仅使用选定声明上的特性
/// </summary>
[AttributeUsage(
    AttributeTargets.Property | AttributeTargets.Field,
    AllowMultiple = false,
    Inherited = false
)]
public sealed class OrdinalAttribute(int ordinal) : Attribute
{
    public int Ordinal { get; } = ordinal;
}
