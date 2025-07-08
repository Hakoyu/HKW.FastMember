namespace HKW.FastMember;

/// <summary>
/// 强调在 <see cref="System.Data.IDataReader"/> 实例中使用的列位置。
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
public class OrdinalAttribute : Attribute
{
    /// <summary>
    /// 创建 <see cref="OrdinalAttribute"/> 类的新实例。
    /// </summary>
    /// <param name="ordinal">序号值</param>
    public OrdinalAttribute(ushort ordinal)
    {
        Ordinal = ordinal;
    }

    /// <summary>
    /// 在 <see cref="System.Data.IDataReader"/> 实例中使用的列序号。
    /// </summary>
    public ushort Ordinal { get; private set; }
}
