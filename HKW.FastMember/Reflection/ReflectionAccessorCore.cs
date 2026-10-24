using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Reflection.Emit;
using HKW.FastMember.Abstractions;
using Accessor = HKW.FastMember.Abstractions.TypeAccessor;

namespace HKW.FastMember.Reflection;

/// <summary>
/// 供动态生成的反射访问器使用的实现基类, 请通过 <see cref="TypeAccessor.Create(Type)"/> 获取访问器
/// </summary>
public abstract class ReflectionAccessorCore : Accessor
{
    /// <summary>
    /// 使用后端已验证并排序的元数据初始化动态生成的访问器
    /// </summary>
    protected ReflectionAccessorCore(Type type, Member[] members, bool createNewSupported)
        : base(type, members, createNewSupported) { }

    internal static object? GetValue(Accessor accessor, object target, int index) =>
        ((ReflectionAccessorCore)accessor).GetValueCore(target, index);

    internal static void SetValue(Accessor accessor, object target, int index, object? value) =>
        ((ReflectionAccessorCore)accessor).SetValueCore(target, index, value);

    private static readonly object ModuleLock = new();
    private static ModuleBuilder? _module;
    private static int _counter;

    [RequiresDynamicCode(DeploymentRequirements.DynamicCode)]
    [RequiresUnreferencedCode(DeploymentRequirements.UnreferencedCode)]
    internal static Accessor Create(ReflectionModel model) =>
        model.IsPublic ? EmitType(model) : EmitDelegates(model);

    [RequiresDynamicCode(DeploymentRequirements.DynamicCode)]
    private static Accessor EmitDelegates(ReflectionModel model)
    {
        var getter = new DynamicMethod(
            "get_member",
            typeof(object),
            [typeof(object), typeof(int)],
            typeof(ReflectionAccessorCore).Module,
            true
        );
        var setter = new DynamicMethod(
            "set_member",
            null,
            [typeof(object), typeof(int), typeof(object)],
            typeof(ReflectionAccessorCore).Module,
            true
        );
        EmitIndexed(getter.GetILGenerator(), model, false, true);
        EmitIndexed(setter.GetILGenerator(), model, false, false);
        Func<object>? constructor = null;
        if (model.Constructor is not null)
        {
            var method = new DynamicMethod(
                "create_instance",
                typeof(object),
                Type.EmptyTypes,
                typeof(ReflectionAccessorCore).Module,
                true
            );
            EmitConstructor(method.GetILGenerator(), model.Constructor);
            constructor = method.CreateDelegate<Func<object>>();
        }
        return new DelegateAccessor(
            model,
            getter.CreateDelegate<Func<object, int, object?>>(),
            setter.CreateDelegate<Action<object, int, object?>>(),
            constructor
        );
    }

    [RequiresDynamicCode(DeploymentRequirements.DynamicCode)]
    [RequiresUnreferencedCode(DeploymentRequirements.UnreferencedCode)]
    private static Accessor EmitType(ReflectionModel model)
    {
        lock (ModuleLock)
        {
            _module ??= AssemblyBuilder
                .DefineDynamicAssembly(
                    new AssemblyName("FastMember_dynamic"),
                    AssemblyBuilderAccess.Run
                )
                .DefineDynamicModule("FastMember_dynamic");
            var type = _module.DefineType(
                $"FastMember_dynamic.Accessor_{++_counter}",
                TypeAttributes.Public | TypeAttributes.Sealed,
                typeof(ReflectionAccessorCore)
            );
            var ctor = type.DefineConstructor(
                MethodAttributes.Public,
                CallingConventions.Standard,
                [typeof(Type), typeof(Member[]), typeof(bool)]
            );
            var il = ctor.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Ldarg_3);
            il.Emit(
                OpCodes.Call,
                typeof(ReflectionAccessorCore).GetConstructor(
                    BindingFlags.Instance | BindingFlags.NonPublic,
                    null,
                    [typeof(Type), typeof(Member[]), typeof(bool)],
                    null
                )!
            );
            il.Emit(OpCodes.Ret);

            EmitOverride(
                type,
                nameof(GetValueCore),
                typeof(object),
                [typeof(object), typeof(int)],
                body => EmitIndexed(body, model, true, true)
            );
            EmitOverride(
                type,
                nameof(SetValueCore),
                typeof(void),
                [typeof(object), typeof(int), typeof(object)],
                body => EmitIndexed(body, model, true, false)
            );
            if (model.Constructor is not null)
                EmitOverride(
                    type,
                    nameof(CreateNewCore),
                    typeof(object),
                    Type.EmptyTypes,
                    body => EmitConstructor(body, model.Constructor)
                );
            return (Accessor)
                Activator.CreateInstance(
                    type.CreateType(),
                    model.Type,
                    model.Members,
                    model.Constructor is not null
                )!;
        }
    }

    private static void EmitOverride(
        TypeBuilder type,
        string name,
        Type result,
        Type[] parameters,
        Action<ILGenerator> emit
    )
    {
        var method = type.DefineMethod(
            name,
            MethodAttributes.Family | MethodAttributes.Virtual | MethodAttributes.HideBySig,
            result,
            parameters
        );
        emit(method.GetILGenerator());
        type.DefineMethodOverride(
            method,
            typeof(Accessor).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!
        );
    }

    private static void EmitConstructor(ILGenerator il, ConstructorInfo constructor)
    {
        il.Emit(OpCodes.Newobj, constructor);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitIndexed(ILGenerator il, ReflectionModel model, bool instance, bool get)
    {
        var labels = model.Members.Select(_ => il.DefineLabel()).ToArray();
        var failure = il.DefineLabel();
        LoadArgument(il, instance ? 2 : 1);
        il.Emit(OpCodes.Switch, labels);
        il.Emit(OpCodes.Br, failure);
        for (var i = 0; i < labels.Length; i++)
        {
            il.MarkLabel(labels[i]);
            var member = model.Members[i];
            if ((get ? member.CanRead : member.CanWrite) is false)
            {
                il.Emit(OpCodes.Br, failure);
                continue;
            }
            var declaring = member.MemberInfo.DeclaringType!;
            LoadArgument(il, instance ? 1 : 0);
            il.Emit(model.Type.IsValueType ? OpCodes.Unbox : OpCodes.Castclass, model.Type);
            if (member.MemberInfo is FieldInfo field)
            {
                if (get)
                    il.Emit(OpCodes.Ldfld, field);
                else
                {
                    LoadArgument(il, instance ? 3 : 2);
                    CastValue(il, member.Type);
                    il.Emit(OpCodes.Stfld, field);
                }
            }
            else
            {
                var method = get ? member.Getter! : member.Setter!;
                var call = declaring.IsValueType ? OpCodes.Call : OpCodes.Callvirt;
                if (get || member.IsByRef)
                    il.EmitCall(call, method, null);
                if (get && member.IsByRef)
                    il.Emit(OpCodes.Ldobj, member.Type);
                if (!get)
                {
                    LoadArgument(il, instance ? 3 : 2);
                    CastValue(il, member.Type);
                    if (member.IsByRef)
                        il.Emit(OpCodes.Stobj, member.Type);
                    else
                        il.EmitCall(call, method, null);
                }
            }
            if (get && member.Type.IsValueType)
                il.Emit(OpCodes.Box, member.Type);
            il.Emit(OpCodes.Ret);
        }
        il.MarkLabel(failure);
        il.Emit(OpCodes.Newobj, typeof(NotSupportedException).GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Throw);
    }

    private static void LoadArgument(ILGenerator il, int argument) =>
        il.Emit(OpCodes.Ldarg, (short)argument);

    private static void CastValue(ILGenerator il, Type type) =>
        il.Emit(type.IsValueType ? OpCodes.Unbox_Any : OpCodes.Castclass, type);

    private sealed class DelegateAccessor(
        ReflectionModel model,
        Func<object, int, object?> getter,
        Action<object, int, object?> setter,
        Func<object>? constructor
    ) : ReflectionAccessorCore(model.Type, model.Members, constructor is not null)
    {
        protected override object? GetValueCore(object target, int index) => getter(target, index);

        protected override void SetValueCore(object target, int index, object? value) =>
            setter(target, index, value);

        protected override object CreateNewCore() => constructor!();
    }
}
