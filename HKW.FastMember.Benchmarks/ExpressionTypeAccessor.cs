//using System.Collections.Concurrent;
//using System.Collections.Immutable;
//using System.Linq.Expressions;
//using System.Reflection;

//namespace HKW.FastMember.Benchmarks;

//internal sealed class ExpressionTypeAccessor : TypeAccessor
//{
//    private static readonly ConcurrentDictionary<Type, Lazy<ExpressionTypeAccessor>> Cache = new();
//    private static readonly MethodInfo StringEquals = typeof(string).GetMethod(
//        nameof(string.Equals),
//        [typeof(string), typeof(string)]
//    )!;

//    private readonly Type _type;
//    private readonly ImmutableHashSet<string> _readableMembers;
//    private readonly ImmutableHashSet<string> _writableMembers;
//    private readonly Func<object, string, object> _getter;
//    private readonly Action<object, string, object> _setter;
//    private readonly Func<object>? _constructor;
//    private ImmutableArray<Member>? _members;
//    private ImmutableDictionary<string, Member>? _memberDictionary;

//    private ExpressionTypeAccessor(Type type)
//    {
//        _type = type;
//        var dispatchMembers = type
//            .GetTypeAndInterfaceProperties(BindingFlags.Public | BindingFlags.Instance)
//            .Where(property => property.GetIndexParameters().Length == 0)
//            .Cast<MemberInfo>()
//            .Concat(type.GetFields(BindingFlags.Public | BindingFlags.Instance))
//            .GroupBy(member => member.Name, StringComparer.Ordinal)
//            .Select(group => group.First())
//            .ToImmutableArray();
//        var readableMembers = dispatchMembers.Where(IsReadable).ToImmutableArray();
//        var writableMembers = dispatchMembers.Where(IsWritable).ToImmutableArray();
//        _readableMembers = readableMembers
//            .Select(member => member.Name)
//            .ToImmutableHashSet(StringComparer.Ordinal);
//        _writableMembers = writableMembers
//            .Select(member => member.Name)
//            .ToImmutableHashSet(StringComparer.Ordinal);
//        _getter = CompileGetter(type, readableMembers);
//        _setter = CompileSetter(type, writableMembers);
//        _constructor = CompileConstructor(type);
//    }

//    public static new ExpressionTypeAccessor Create(Type type)
//    {
//        ArgumentNullException.ThrowIfNull(type);
//        return Cache
//            .GetOrAdd(
//                type,
//                static key =>
//                    new Lazy<ExpressionTypeAccessor>(
//                        () => new ExpressionTypeAccessor(key),
//                        LazyThreadSafetyMode.ExecutionAndPublication
//                    )
//            )
//            .Value;
//    }

//    public override bool CreateNewSupported => _constructor is not null;

//    public override bool GetMembersSupported => true;

//    public override object CreateNew() =>
//        _constructor is not null ? _constructor() : base.CreateNew();

//    public override ImmutableArray<Member> GetMembers() =>
//        _members ??= TypeHelpers.GetMembers(_type);

//    public override ImmutableDictionary<string, Member> GetMemberDictionary()
//    {
//        _members ??= TypeHelpers.GetMembers(_type);
//        return _memberDictionary ??= _members.Value.ToImmutableDictionary(
//            member => member.Name,
//            member => member,
//            StringComparer.Ordinal
//        );
//    }

//    public override object this[object target, string name]
//    {
//        get => _getter(target, name);
//        set => _setter(target, name, value);
//    }

//    public override bool TryGetValue(object target, string name, out object value)
//    {
//        if (!_readableMembers.Contains(name))
//        {
//            value = default!;
//            return false;
//        }

//        value = _getter(target, name);
//        return true;
//    }

//    public override bool TrySetValue(object target, string name, object value)
//    {
//        if (!_writableMembers.Contains(name))
//            return false;

//        _setter(target, name, value);
//        return true;
//    }

//    private static Func<object, string, object> CompileGetter(
//        Type type,
//        ImmutableArray<MemberInfo> members
//    )
//    {
//        var target = Expression.Parameter(typeof(object), "target");
//        var name = Expression.Parameter(typeof(string), "name");
//        var typedTarget = Expression.Convert(target, type);
//        Expression body = CreateMissingMemberExpression(type, name, typeof(object));

//        for (var index = members.Length - 1; index >= 0; index--)
//        {
//            var member = members[index];
//            var access = CreateAccessExpression(typedTarget, member);
//            body = Expression.Condition(
//                Expression.Call(StringEquals, name, Expression.Constant(member.Name)),
//                Expression.Convert(access, typeof(object)),
//                body
//            );
//        }

//        return Expression.Lambda<Func<object, string, object>>(body, target, name).Compile();
//    }

//    private static Action<object, string, object> CompileSetter(
//        Type type,
//        ImmutableArray<MemberInfo> members
//    )
//    {
//        var target = Expression.Parameter(typeof(object), "target");
//        var name = Expression.Parameter(typeof(string), "name");
//        var value = Expression.Parameter(typeof(object), "value");
//        var typedTarget = Expression.Convert(target, type);
//        Expression body = CreateMissingMemberExpression(type, name, typeof(void));

//        for (var index = members.Length - 1; index >= 0; index--)
//        {
//            var member = members[index];
//            var access = CreateAccessExpression(typedTarget, member);
//            var assign = Expression.Assign(access, Expression.Convert(value, GetMemberType(member)));
//            body = Expression.IfThenElse(
//                Expression.Call(StringEquals, name, Expression.Constant(member.Name)),
//                Expression.Block(assign, Expression.Empty()),
//                body
//            );
//        }

//        return Expression.Lambda<Action<object, string, object>>(body, target, name, value).Compile();
//    }

//    private static Func<object>? CompileConstructor(Type type)
//    {
//        if (!type.IsClass || type.IsAbstract || type.GetConstructor(Type.EmptyTypes) is null)
//            return null;

//        return Expression
//            .Lambda<Func<object>>(Expression.Convert(Expression.New(type), typeof(object)))
//            .Compile();
//    }

//    private static Expression CreateAccessExpression(Expression target, MemberInfo member) =>
//        member switch
//        {
//            PropertyInfo property => Expression.Property(target, property),
//            FieldInfo field => Expression.Field(target, field),
//            _ => throw new NotSupportedException(member.MemberType.ToString()),
//        };

//    private static Expression CreateMissingMemberExpression(
//        Type type,
//        ParameterExpression name,
//        Type resultType
//    )
//    {
//        var exception = Expression.New(
//            typeof(ArgumentOutOfRangeException).GetConstructor([typeof(string), typeof(string)])!,
//            Expression.Constant("name"),
//            Expression.Call(
//                typeof(string).GetMethod(nameof(string.Concat), [typeof(string), typeof(string)])!,
//                Expression.Constant($"Source type: {type}, Member name: "),
//                name
//            )
//        );
//        return Expression.Throw(exception, resultType);
//    }

//    private static Type GetMemberType(MemberInfo member) =>
//        member switch
//        {
//            PropertyInfo property => property.PropertyType,
//            FieldInfo field => field.FieldType,
//            _ => throw new NotSupportedException(member.MemberType.ToString()),
//        };

//    private static bool IsReadable(MemberInfo member) =>
//        member switch
//        {
//            PropertyInfo property => property.GetMethod is not null && !property.PropertyType.IsByRef,
//            FieldInfo field => !field.FieldType.IsByRef,
//            _ => false,
//        };

//    private static bool IsWritable(MemberInfo member) =>
//        member switch
//        {
//            PropertyInfo property => property.SetMethod is not null && !property.PropertyType.IsByRef,
//            FieldInfo field => !field.IsInitOnly && !field.IsLiteral && !field.FieldType.IsByRef,
//            _ => false,
//        };
//}
