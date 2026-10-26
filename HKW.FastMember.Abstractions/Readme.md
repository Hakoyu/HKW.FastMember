# HKW.FastMember.Abstractions

提供与后端无关的成员访问、不可变元数据，以及共享的同步/异步读取器。

## 包职责边界

三个包采用单向运行时依赖设计：

- `HKW.FastMember` → `HKW.FastMember.Abstractions`：反射发现、Emit 和 DLR 后端。
- `HKW.FastMember.Generater` → `HKW.FastMember.Abstractions`：源生成访问后端，其分析器在包内与运行时程序集分开打包。
- `HKW.FastMember.Abstractions`：通用契约和读取器实现；

## 访问器契约

`TypeAccessor` 支持运行时目标类型，包括装箱值。`TypeAccessor<T> : TypeAccessor`（`where T : class`）新增强类型入口，使用固定的静态 `T` 契约，而不是实例的派生类型。两者均公开不可变的 `TargetType`、`GetMembersSupported`、`CreateNewSupported` 和 `ContractVersion`，并通过 `GetMembers()` 和 `GetMemberDictionary()` 提供只读元数据快照。

实现应提供名称唯一且按 `StringComparer.Ordinal` 名称顺序排列的描述符。非泛型实现重写 `GetValueCore(object, int)`、`SetValueCore(object, int, object?)`，并可选择重写 `CreateNewCore()`。泛型实现则重写 `GetValueTypedCore(T, int)`、`SetValueTypedCore(T, int, object?)`，并可选择重写 `CreateNewTypedCore()`。通用静态成员入口在调用基于索引的核心方法之前，执行一次名称查找并检查权限。

目标或名称为 null 时抛出 `ArgumentNullException`。对于未知或禁止访问的静态成员，索引器抛出 `ArgumentOutOfRangeException`，Try 方法返回 `false`；Try 读取失败时返回的值为 null。用户代码异常和转换异常会向上传播。名称区分大小写，且不会去除首尾空白。不支持构造时抛出 `NotSupportedException`。

基于名称的后端可设置 `getMembersSupported: false`，并重写非泛型索引器/Try 方法。强类型的基于名称的后端使用受保护的四参数重载 `TypeAccessor<T>(members, createNewSupported, contractVersion, getMembersSupported)`；原有的三参数构造函数仍支持静态元数据。强类型索引器/Try 入口通过同一组非泛型虚拟成员分派调用。此时元数据查询会抛出 `NotSupportedException`，而不是返回具有误导性的空表。后端特有的动态绑定和成员缺失异常仍由后端负责。

## 元数据与绑定对象

`Member` 是纯描述符，具有不可变的 `Name`、`Type`、`Ordinal`、`CanRead`、`CanWrite`、`DeclaringIdentity`、`EffectiveType` 和 `AllowDBNull` 属性。子类可携带后端特有的信息，而不改变通用属性。有效类型和可空性由后端提供，而非由本包发现。

`HKW.FastMember.Abstractions.OrdinalAttribute` 是属性和字段唯一的共享排序特性。请将旧命名空间的导入迁移至此特性；不提供旧版特性的别名。

绑定对象时，参数顺序为**源对象在前，访问器在后**：

```csharp
var bound = new ObjectAccessor(source, accessor);
var typedBound = new ObjectAccessor<Model>(model, typedAccessor);
```

构造函数要求源对象和访问器均非 null，并拒绝不兼容的源类型。`ObjectAccessor<T>` 派生自通用包装器，并公开强类型的只读 `Source`。两种包装器均复用访问器的操作和元数据，始终不替换源对象，并将 `Equals`、`GetHashCode` 和 `ToString` 直接委托给源对象。

## 读取器注入

通用读取器工厂接收显式提供的非泛型 `TypeAccessor`（也接受 `TypeAccessor<T>`）：

```csharp
using HKW.FastMember.Abstractions;

using var reader = ObjectReader.Create(rows, accessor, "Id", "Name");
await using var asyncReader = ObjectReader.Create(asyncRows, accessor, cancellationToken, "Id", "Name");
```

API 参数顺序为**源、访问器、可选取消令牌，最后是 `params string[]` 列名**。访问器的 `TargetType` 必须能够接受源的静态元素类型；不兼容的组合会在枚举前失败。源元素 `T` 没有 `class` 约束，因此可通过非泛型访问器支持同步和异步结构体数据源；读取值类型时可能发生装箱。

省略列名时，选择可读成员，先按序号再按名称排序。显式指定的列保留顺序和重复项；未知或不可读的列会在创建时被拒绝。列名数组或列名为 null 时会被拒绝。即使数据源为空，也无需枚举即可获取架构信息。创建时的取消令牌会传递给异步枚举器；每次读取的令牌会在读取前检查。对异步源执行同步操作可能会阻塞。

`GetMembersSupported == false` 的访问器无法直接传递给通用泛型读取器工厂，即使显式指定列名也不行。必须先由后端特定的列适配器提供固定的可读架构。显式 DLR 列的适配由反射后端负责；本包不会发现动态列，也不会合并运行时行类型的架构。
