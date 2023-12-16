using System.Reflection;

namespace HKW.FastMember;

internal static class TypeHelpers
{
    /// <summary>
    /// If type is a class, get its properties; if type is an interface, get its
    /// properties plus the properties of all the interfaces it inherits.
    /// </summary>
    /// <param name="type"></param>
    /// <param name="flags"></param>
    /// <returns></returns>
    public static PropertyInfo[] GetTypeAndInterfaceProperties(this Type type, BindingFlags flags)
    {
        return !type.IsInterface
            ? type.GetProperties(flags)
            : (new[] { type })
                .Concat(type.GetInterfaces())
                .SelectMany(i => i.GetProperties(flags))
                .ToArray();
    }
}
