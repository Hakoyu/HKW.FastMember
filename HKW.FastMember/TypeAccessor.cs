using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Dynamic;
using System.Reflection;
using System.Reflection.Emit;

namespace HKW.FastMember;

/// <summary>
/// 提供对给定类型对象的按名称成员访问
/// </summary>
public abstract class TypeAccessor
{
    /// <summary>
    /// 此类型是否支持通过无参构造函数创建新实例？
    /// </summary>
    public virtual bool CreateNewSupported => false;

    /// <summary>
    /// 此类型是否可以查询成员可用性？
    /// </summary>
    public virtual bool GetMembersSupported => false;

    // 哈希表比字典有更好的无锁读取语义
    private static readonly ConcurrentDictionary<Type, TypeAccessor> _publicAccessors = new(),
        _nonPublicAccessors = new();

    private static AssemblyBuilder? _assembly;
    private static ModuleBuilder? _module;
    private static int _counter;

    /// <summary>
    /// 创建此类型的新实例
    /// </summary>
    public virtual object CreateNew()
    {
        throw new NotSupportedException();
    }

    /// <summary>
    /// 查询此类型可用的成员
    /// </summary>
    public virtual Member[] GetMembers()
    {
        throw new NotSupportedException();
    }

    /// <summary>
    /// 提供类型特定的访问器，允许按名称访问该类型的所有对象
    /// </summary>
    /// <remarks>访问器在内部缓存；可能返回预先存在的访问器</remarks>
    public static TypeAccessor Create(Type type)
    {
        return Create(type, false);
    }

    /// <summary>
    /// 提供类型特定的访问器，允许按名称访问该类型的所有对象
    /// </summary>
    /// <remarks>访问器在内部缓存；可能返回预先存在的访问器</remarks>
    public static TypeAccessor Create(Type type, bool allowNonPublicAccessors)
    {
        ArgumentNullException.ThrowIfNull(type, nameof(type));

        var accessors = allowNonPublicAccessors ? _nonPublicAccessors : _publicAccessors;
        if (accessors.TryGetValue(type, out var accessor))
            return accessor;

        return accessors[type] = CreateNew(type, allowNonPublicAccessors);
    }

    private static int GetNextCounterValue()
    {
        return Interlocked.Increment(ref _counter);
    }

    static readonly MethodInfo _tryGetValue = typeof(Dictionary<string, int>).GetMethod(
        nameof(Dictionary<string, int>.TryGetValue)
    )!;

    private static void WriteMapImpl(
        ILGenerator il,
        Type type,
        List<MemberInfo> members,
        FieldBuilder? mapField,
        bool allowNonPublicAccessors,
        bool isGet
    )
    {
        OpCode obj,
            index,
            value;

        Label fail = il.DefineLabel();
        if (mapField == null)
        {
            index = OpCodes.Ldarg_0;
            obj = OpCodes.Ldarg_1;
            value = OpCodes.Ldarg_2;
        }
        else
        {
            il.DeclareLocal(typeof(int));
            index = OpCodes.Ldloc_0;
            obj = OpCodes.Ldarg_1;
            value = OpCodes.Ldarg_3;

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, mapField);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Ldloca_S, (byte)0);
            il.EmitCall(OpCodes.Callvirt, _tryGetValue, null);
            il.Emit(OpCodes.Brfalse, fail);
        }
        var labels = new Label[members.Count];
        for (int i = 0; i < labels.Length; i++)
        {
            labels[i] = il.DefineLabel();
        }
        il.Emit(index);
        il.Emit(OpCodes.Switch, labels);
        il.MarkLabel(fail);
        il.Emit(OpCodes.Ldstr, "name");
        il.Emit(
            OpCodes.Newobj,
            typeof(ArgumentOutOfRangeException).GetConstructor([typeof(string)])!
        );
        il.Emit(OpCodes.Throw);
        for (var i = 0; i < labels.Length; i++)
        {
            il.MarkLabel(labels[i]);
            var member = members[i];
            bool isFail = true;

            void WriteField(FieldInfo fieldToWrite)
            {
                if (!fieldToWrite.FieldType.IsByRef)
                {
                    il.Emit(obj);
                    Cast(il, type, true);
                    if (isGet)
                    {
                        il.Emit(OpCodes.Ldfld, fieldToWrite);
                        if (fieldToWrite.FieldType.IsValueType)
                            il.Emit(OpCodes.Box, fieldToWrite.FieldType);
                    }
                    else
                    {
                        il.Emit(value);
                        Cast(il, fieldToWrite.FieldType, false);
                        il.Emit(OpCodes.Stfld, fieldToWrite);
                    }
                    il.Emit(OpCodes.Ret);
                    isFail = false;
                }
            }
            if (member is FieldInfo field)
            {
                WriteField(field);
            }
            else if (member is PropertyInfo prop)
            {
                var propType = prop.PropertyType;
                var isByRef = propType.IsByRef;
                var isValid = true;
                if (isByRef)
                {
                    if (
                        !isGet
                        && prop.CustomAttributes.Any(x =>
                            x.AttributeType.FullName
                            == "System.Runtime.CompilerServices.IsReadOnlyAttribute"
                        )
                    )
                    {
                        isValid = false; // 无法间接赋值给只读引用
                    }
                    propType = propType.GetElementType(); // 从 "ref Foo" 转换为 "Foo"
                }

                var accessor =
                    (isGet | isByRef)
                        ? prop.GetGetMethod(allowNonPublicAccessors)
                        : prop.GetSetMethod(allowNonPublicAccessors);
                if (accessor == null && allowNonPublicAccessors && !isByRef)
                {
                    // 没有getter/setter，如果存在则使用后备字段
                    var backingField = $"<{prop.Name}>k__BackingField";
                    field = prop.DeclaringType?.GetField(
                        backingField,
                        BindingFlags.Instance | BindingFlags.NonPublic
                    )!;

                    if (field is not null)
                    {
                        WriteField(field);
                    }
                }
                else if (isValid && prop.CanRead && accessor != null)
                {
                    il.Emit(obj);
                    Cast(il, type, true); // 将输入对象转换为正确的目标类型

                    if (isGet)
                    {
                        il.EmitCall(
                            type.IsValueType ? OpCodes.Call : OpCodes.Callvirt,
                            accessor,
                            null
                        );
                        if (isByRef)
                            il.Emit(OpCodes.Ldobj, propType!); // 如果需要则解引用
                        if (propType!.IsValueType)
                            il.Emit(OpCodes.Box, propType); // 如果需要则装箱值类型
                    }
                    else
                    {
                        // 当为引用类型时，我们首先获取目标托管指针，即将 obj.TheRef 放在堆栈上
                        if (isByRef)
                            il.EmitCall(
                                type.IsValueType ? OpCodes.Call : OpCodes.Callvirt,
                                accessor,
                                null
                            );

                        // 加载新值并指定类型
                        il.Emit(value);
                        Cast(il, propType!, false);

                        if (isByRef)
                        { // 赋值给托管指针
                            il.Emit(OpCodes.Stobj, propType!);
                        }
                        else
                        { // 调用setter
                            il.EmitCall(
                                type.IsValueType ? OpCodes.Call : OpCodes.Callvirt,
                                accessor,
                                null
                            );
                        }
                    }
                    il.Emit(OpCodes.Ret);
                    isFail = false;
                }
            }
            if (isFail)
                il.Emit(OpCodes.Br, fail);
        }
    }

    //private static readonly MethodInfo strinqEquals = typeof(string).GetMethod(
    //    "op_Equality",
    //    new Type[] { typeof(string), typeof(string) }
    //);

    /// <summary>
    /// 基于类型实现的TypeAccessor，具有可用的成员元数据
    /// </summary>
    protected abstract class RuntimeTypeAccessor : TypeAccessor
    {
        /// <summary>
        /// 返回此访问器表示的类型
        /// </summary>
        protected abstract Type Type { get; }

        /// <summary>
        /// 此类型是否可以查询成员可用性？
        /// </summary>
        public override bool GetMembersSupported => true;

        private Member[] _members = null!;

        /// <summary>
        /// 查询此类型可用的成员
        /// </summary>
        public override Member[] GetMembers()
        {
            return _members ??= TypeHelpers.GetMembers(Type);
        }
    }

    sealed class DelegateAccessor : RuntimeTypeAccessor
    {
        protected override Type Type { get; }

        private readonly ImmutableDictionary<string, int> _dic;
        private readonly Func<int, object, object> _getter;
        private readonly Action<int, object, object> _setter;
        private readonly Func<object>? _ctor;

        public DelegateAccessor(
            ImmutableDictionary<string, int> dic,
            Func<int, object, object> getter,
            Action<int, object, object> setter,
            Func<object>? ctor,
            Type type
        )
        {
            _dic = dic;
            _getter = getter;
            _setter = setter;
            _ctor = ctor;
            Type = type;
        }

        public override bool CreateNewSupported => _ctor != null;

        public override object CreateNew()
        {
            return _ctor != null ? _ctor() : base.CreateNew();
        }

        public override object this[object target, string name]
        {
            get
            {
                if (_dic.TryGetValue(name, out int index))
                    return _getter(index, target);
                else
                    throw new ArgumentOutOfRangeException(nameof(name));
            }
            set
            {
                if (_dic.TryGetValue(name, out int index))
                    _setter(index, target, value);
                else
                    throw new ArgumentOutOfRangeException(nameof(name));
            }
        }

        public override bool TryGetValue(object target, string name, out object value)
        {
            if (_dic.TryGetValue(name, out int index) is false)
            {
                value = default!;
                return false;
            }
            else
            {
                value = _getter(index, target);
                return true;
            }
        }

        public override bool TrySetValue(object target, string name, object value)
        {
            if (_dic.TryGetValue(name, out int index) is false)
                return false;
            else
            {
                _setter(index, target, value);
                return true;
            }
        }
    }

    private static bool IsFullyPublic(Type type, PropertyInfo[] props, bool allowNonPublicAccessors)
    {
        while (type.IsNestedPublic)
            type = type.DeclaringType!;
        if (!type.IsPublic)
            return false;

        if (allowNonPublicAccessors)
        {
            for (int i = 0; i < props.Length; i++)
            {
                // 非公开的getter
                if (props[i].GetGetMethod(true) != null && props[i].GetGetMethod(false) == null)
                    return false;
                // 非公开的setter
                if (props[i].GetSetMethod(true) != null && props[i].GetSetMethod(false) == null)
                    return false;
            }
        }

        return true;
    }

    private static TypeAccessor CreateNew(Type type, bool allowNonPublicAccessors)
    {
        if (typeof(IDynamicMetaObjectProvider).IsAssignableFrom(type))
        {
            return DynamicAccessor.Singleton;
        }

        var props = type.GetTypeAndInterfaceProperties(BindingFlags.Public | BindingFlags.Instance);
        var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance);
        var dic = new Dictionary<string, int>();
        var members = new List<MemberInfo>(props.Length + fields.Length);
        int i = 0;
        foreach (var prop in props)
        {
            if (!dic.ContainsKey(prop.Name) && prop.GetIndexParameters().Length == 0)
            {
                dic.Add(prop.Name, i++);
                members.Add(prop);
            }
        }

        foreach (var field in fields)
        {
            if (!dic.ContainsKey(field.Name))
            {
                dic.Add(field.Name, i++);
                members.Add(field);
            }
        }

        ConstructorInfo ctor = null!;

        if (type.IsClass && !type.IsAbstract)
        {
            ctor = type.GetConstructor(Type.EmptyTypes)!;
        }

        ILGenerator il;
        if (!IsFullyPublic(type, props, allowNonPublicAccessors))
        {
            var dynGetter = new DynamicMethod(
                type.FullName + "_get",
                typeof(object),
                new Type[] { typeof(int), typeof(object) },
                type,
                true
            );
            var dynSetter = new DynamicMethod(
                type.FullName + "_set",
                null,
                new Type[] { typeof(int), typeof(object), typeof(object) },
                type,
                true
            );
            WriteMapImpl(
                dynGetter.GetILGenerator(),
                type,
                members,
                null,
                allowNonPublicAccessors,
                true
            );
            WriteMapImpl(
                dynSetter.GetILGenerator(),
                type,
                members,
                null,
                allowNonPublicAccessors,
                false
            );
            DynamicMethod? dynCtor = null;
            if (ctor != null)
            {
                dynCtor = new DynamicMethod(
                    type.FullName + "_ctor",
                    typeof(object),
                    Type.EmptyTypes,
                    type,
                    true
                );
                il = dynCtor.GetILGenerator();
                il.Emit(OpCodes.Newobj, ctor);
                il.Emit(OpCodes.Ret);
            }
            return new DelegateAccessor(
                dic.ToImmutableDictionary(),
                (Func<int, object, object>)
                    dynGetter.CreateDelegate(typeof(Func<int, object, object>)),
                (Action<int, object, object>)
                    dynSetter.CreateDelegate(typeof(Action<int, object, object>)),
                dynCtor is null ? null : (Func<object>)dynCtor.CreateDelegate(typeof(Func<object>)),
                type
            );
        }

        // note 注意此区域是同步的；一次只创建一个，所以不需要担心构建器
        if (_assembly == null)
        {
            var name = new AssemblyName("FastMember_dynamic");
            _assembly = AssemblyBuilder.DefineDynamicAssembly(name, AssemblyBuilderAccess.Run);
            _module = _assembly.DefineDynamicModule(name.Name!);
        }
        TypeAttributes attribs = typeof(TypeAccessor).Attributes;
        TypeBuilder tb = _module!.DefineType(
            "FastMember_dynamic." + type.Name + "_" + GetNextCounterValue(),
            (attribs | TypeAttributes.Sealed | TypeAttributes.Public)
                & ~(TypeAttributes.Abstract | TypeAttributes.NotPublic),
            typeof(RuntimeTypeAccessor)
        );

        il = tb.DefineConstructor(
                MethodAttributes.Public,
                CallingConventions.Standard,
                new[] { typeof(Dictionary<string, int>) }
            )
            .GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        FieldBuilder mapField = tb.DefineField(
            "_dic",
            typeof(Dictionary<string, int>),
            FieldAttributes.InitOnly | FieldAttributes.Private
        );
        il.Emit(OpCodes.Stfld, mapField);
        il.Emit(OpCodes.Ret);

        PropertyInfo indexer = typeof(TypeAccessor).GetProperty("Item")!;
        var baseGetter = indexer.GetGetMethod()!;
        var baseSetter = indexer.GetSetMethod()!;
        var body = tb.DefineMethod(
            baseGetter.Name,
            baseGetter.Attributes & ~MethodAttributes.Abstract,
            typeof(object),
            new Type[] { typeof(object), typeof(string) }
        );
        il = body.GetILGenerator();
        WriteMapImpl(il, type, members, mapField, allowNonPublicAccessors, true);
        tb.DefineMethodOverride(body, baseGetter);

        body = tb.DefineMethod(
            baseSetter.Name,
            baseSetter.Attributes & ~MethodAttributes.Abstract,
            null,
            new Type[] { typeof(object), typeof(string), typeof(object) }
        );
        il = body.GetILGenerator();
        WriteMapImpl(il, type, members, mapField, allowNonPublicAccessors, false);
        tb.DefineMethodOverride(body, baseSetter);

        MethodInfo baseMethod;
        if (ctor != null)
        {
            baseMethod = typeof(TypeAccessor).GetProperty("CreateNewSupported")!.GetGetMethod()!;
            body = tb.DefineMethod(
                baseMethod.Name,
                baseMethod.Attributes,
                baseMethod.ReturnType,
                Type.EmptyTypes
            );
            il = body.GetILGenerator();
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Ret);
            tb.DefineMethodOverride(body, baseMethod);

            baseMethod = typeof(TypeAccessor).GetMethod("CreateNew")!;
            body = tb.DefineMethod(
                baseMethod.Name,
                baseMethod.Attributes,
                baseMethod.ReturnType,
                Type.EmptyTypes
            );
            il = body.GetILGenerator();
            il.Emit(OpCodes.Newobj, ctor);
            il.Emit(OpCodes.Ret);
            tb.DefineMethodOverride(body, baseMethod);
        }

        baseMethod = typeof(RuntimeTypeAccessor)
            .GetProperty("Type", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetGetMethod(true)!;
        body = tb.DefineMethod(
            baseMethod.Name,
            baseMethod.Attributes & ~MethodAttributes.Abstract,
            baseMethod.ReturnType,
            Type.EmptyTypes
        );
        il = body.GetILGenerator();
        il.Emit(OpCodes.Ldtoken, type);
        il.Emit(OpCodes.Call, typeof(Type).GetMethod("GetTypeFromHandle")!);
        il.Emit(OpCodes.Ret);
        tb.DefineMethodOverride(body, baseMethod);

        // 添加 TryGetValue 方法的 IL 编织
        baseMethod = typeof(TypeAccessor).GetMethod("TryGetValue")!;
        body = tb.DefineMethod(
            baseMethod.Name,
            baseMethod.Attributes & ~MethodAttributes.Abstract,
            baseMethod.ReturnType,
            new Type[] { typeof(object), typeof(string), typeof(object).MakeByRefType() }
        );
        il = body.GetILGenerator();
        WriteTryMapImpl(il, type, members, mapField, allowNonPublicAccessors, true);
        tb.DefineMethodOverride(body, baseMethod);

        // 添加 TrySetValue 方法的 IL 编织
        baseMethod = typeof(TypeAccessor).GetMethod("TrySetValue")!;
        body = tb.DefineMethod(
            baseMethod.Name,
            baseMethod.Attributes & ~MethodAttributes.Abstract,
            baseMethod.ReturnType,
            new Type[] { typeof(object), typeof(string), typeof(object) }
        );
        il = body.GetILGenerator();
        WriteTryMapImpl(il, type, members, mapField, allowNonPublicAccessors, false);
        tb.DefineMethodOverride(body, baseMethod);

        var accessor = (TypeAccessor)Activator.CreateInstance(tb.CreateTypeInfo().AsType(), dic)!;
        return accessor;
    }

    private static void WriteTryMapImpl(
        ILGenerator il,
        Type type,
        List<MemberInfo> members,
        FieldBuilder mapField,
        bool allowNonPublicAccessors,
        bool isGet
    )
    {
        // 声明局部变量：int index
        il.DeclareLocal(typeof(int));

        // 加载字典、名称参数并调用 TryGetValue
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, mapField);
        il.Emit(OpCodes.Ldarg_2); // name 参数
        il.Emit(OpCodes.Ldloca_S, (byte)0); // index 局部变量的地址
        il.EmitCall(OpCodes.Callvirt, _tryGetValue, null);

        // 如果 TryGetValue 返回 false，跳转到返回 false
        Label returnFalse = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, returnFalse);

        var labels = new Label[members.Count];
        for (int i = 0; i < labels.Length; i++)
        {
            labels[i] = il.DefineLabel();
        }

        // switch 基于索引
        il.Emit(OpCodes.Ldloc_0); // 加载 index
        il.Emit(OpCodes.Switch, labels);

        // 如果索引超出范围，返回 false
        il.MarkLabel(returnFalse);
        if (isGet)
        {
            il.Emit(OpCodes.Ldarg_3); // value 参数
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Stind_Ref); // 设置 value = null
        }
        il.Emit(OpCodes.Ldc_I4_0); // 返回 false
        il.Emit(OpCodes.Ret);

        // 为每个成员生成逻辑
        for (var i = 0; i < labels.Length; i++)
        {
            il.MarkLabel(labels[i]);
            var member = members[i];
            bool hasValidOperation = false;

            void WriteField(FieldInfo fieldToAccess)
            {
                if (!fieldToAccess.FieldType.IsByRef)
                {
                    if (isGet)
                    {
                        il.Emit(OpCodes.Ldarg_3); // value 参数
                        il.Emit(OpCodes.Ldarg_1); // target 参数
                        Cast(il, type, true);
                        il.Emit(OpCodes.Ldfld, fieldToAccess);
                        if (fieldToAccess.FieldType.IsValueType)
                            il.Emit(OpCodes.Box, fieldToAccess.FieldType);
                        il.Emit(OpCodes.Stind_Ref); // 设置 value
                    }
                    else
                    {
                        il.Emit(OpCodes.Ldarg_1); // target 参数
                        Cast(il, type, true);
                        il.Emit(OpCodes.Ldarg_3); // value 参数
                        Cast(il, fieldToAccess.FieldType, false);
                        il.Emit(OpCodes.Stfld, fieldToAccess);
                    }
                    il.Emit(OpCodes.Ldc_I4_1); // 返回 true
                    il.Emit(OpCodes.Ret);
                    hasValidOperation = true;
                }
            }

            if (member is FieldInfo field)
            {
                WriteField(field);
            }
            else if (member is PropertyInfo prop)
            {
                var propType = prop.PropertyType;
                var isByRef = propType.IsByRef;
                var isValid = true;

                if (isByRef)
                {
                    if (
                        !isGet
                        && prop.CustomAttributes.Any(x =>
                            x.AttributeType.FullName
                            == "System.Runtime.CompilerServices.IsReadOnlyAttribute"
                        )
                    )
                    {
                        isValid = false; // 无法间接赋值给只读引用
                    }
                    propType = propType.GetElementType();
                }

                var getter = prop.GetGetMethod(allowNonPublicAccessors);
                var setter = prop.GetSetMethod(allowNonPublicAccessors);
                var accessor = isGet || isByRef ? getter : setter;

                if (accessor == null && allowNonPublicAccessors && !isByRef)
                {
                    // 尝试使用后备字段
                    var backingField = $"<{prop.Name}>k__BackingField";
                    field = prop.DeclaringType?.GetField(
                        backingField,
                        BindingFlags.Instance | BindingFlags.NonPublic
                    )!;

                    if (field is not null)
                    {
                        WriteField(field);
                    }
                }
                else if (
                    isValid
                    && accessor != null
                    && (isGet ? prop.CanRead : (prop.CanWrite || (isByRef && getter != null)))
                )
                {
                    if (isGet)
                    {
                        il.Emit(OpCodes.Ldarg_3); // value 参数
                        il.Emit(OpCodes.Ldarg_1); // target 参数
                        Cast(il, type, true);
                        il.EmitCall(
                            type.IsValueType ? OpCodes.Call : OpCodes.Callvirt,
                            accessor,
                            null
                        );
                        if (isByRef)
                            il.Emit(OpCodes.Ldobj, propType!);
                        if (propType!.IsValueType)
                            il.Emit(OpCodes.Box, propType);
                        il.Emit(OpCodes.Stind_Ref); // 设置 value
                    }
                    else
                    {
                        il.Emit(OpCodes.Ldarg_1); // target 参数
                        Cast(il, type, true);

                        if (isByRef)
                        {
                            // 获取引用
                            il.EmitCall(
                                type.IsValueType ? OpCodes.Call : OpCodes.Callvirt,
                                getter!,
                                null
                            );
                            il.Emit(OpCodes.Ldarg_3); // value 参数
                            Cast(il, propType!, false);
                            il.Emit(OpCodes.Stobj, propType!);
                        }
                        else
                        {
                            il.Emit(OpCodes.Ldarg_3); // value 参数
                            Cast(il, propType!, false);
                            il.EmitCall(
                                type.IsValueType ? OpCodes.Call : OpCodes.Callvirt,
                                accessor,
                                null
                            );
                        }
                    }
                    il.Emit(OpCodes.Ldc_I4_1); // 返回 true
                    il.Emit(OpCodes.Ret);
                    hasValidOperation = true;
                }
            }

            if (!hasValidOperation)
            {
                // 如果没有有效的操作，返回 false
                il.Emit(OpCodes.Br, returnFalse);
            }
        }
    }

    private static void Cast(ILGenerator il, Type type, bool valueAsPointer)
    {
        if (type == typeof(object)) { }
        else if (type.IsValueType)
        {
            if (valueAsPointer)
            {
                il.Emit(OpCodes.Unbox, type);
            }
            else
            {
                il.Emit(OpCodes.Unbox_Any, type);
            }
        }
        else
        {
            il.Emit(OpCodes.Castclass, type);
        }
    }

    /// <summary>
    /// 获取或设置目标实例上命名成员的值
    /// </summary>
    public abstract object this[object target, string name] { get; set; }

    /// <summary>
    /// 尝试获取成员值
    /// </summary>
    /// <param name="target">目标</param>
    /// <param name="name">成员名称</param>
    /// <param name="value">成员值</param>
    /// <returns>成功为 <see langword="true"/>, 失败为 <see langword="false"/></returns>
    public abstract bool TryGetValue(object target, string name, out object value);

    /// <summary>
    /// 尝试设置成员值
    /// </summary>
    /// <param name="target">目标</param>
    /// <param name="name">成员名称</param>
    /// <param name="value">成员值</param>
    /// <returns>成功为 <see langword="true"/>, 失败为 <see langword="false"/></returns>
    public abstract bool TrySetValue(object target, string name, object value);
}

internal sealed class DynamicAccessor : TypeAccessor
{
    public static DynamicAccessor Singleton { get; } = new();

    private DynamicAccessor() { }

    public override object this[object target, string name]
    {
        get => CallSiteCache.GetValue(name, target);
        set => CallSiteCache.SetValue(name, target, value);
    }

    public override bool TryGetValue(object target, string name, out object value)
    {
        throw new NotImplementedException();
    }

    public override bool TrySetValue(object target, string name, object value)
    {
        throw new NotImplementedException();
    }
}
