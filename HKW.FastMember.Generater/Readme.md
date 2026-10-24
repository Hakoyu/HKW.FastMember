# HKW.FastMember.Generater

编译期源生成成员访问后端，目标框架为 **net8.0 / net10.0**。NuGet 包名与工厂命名空间均为 `HKW.FastMember.Generater`，开发版本 `0.1.0`。

一个后端包包含运行时程序集和 Roslyn 生成器，正常依赖 [HKW.FastMember.Abstractions](../HKW.FastMember.Abstractions/Readme.md)，不引用反射后端 `HKW.FastMember`。公共访问器、`Member`、`OrdinalAttribute` 及唯一的同步/异步读取器实现位于 `Abstractions`；本包只保留工厂、生成访问核心和注册。这是公共类型命名空间的破坏性重构，不提供旧类型转发。访问代码由消费者的具体类型调用触发生成，**没有 `Reflection.Emit`、动态语言运行时、反射成员访问或 `Expression.Compile` 后备**。

> 当前验收范围仅为双目标框架单元测试（含 `GeneratorDriver` 输出编译和运行）。实际 `.nupkg` 包消费、SDK 宿主矩阵、裁剪、NativeAOT 发布和性能尚未验证，不承诺预先编译安全性或比 `Emit` 更快。此变更不会发布 NuGet 包。

## 泛型访问器

```csharp
using HKW.FastMember.Abstractions;
using GeneratedTypes = HKW.FastMember.Generater.TypeAccessor;
using GeneratedObjects = HKW.FastMember.Generater.ObjectAccessor;

var user = new User();
TypeAccessor<User> accessor = GeneratedTypes.Create<User>();
accessor[user, "Id"] = 42;
accessor[user, "Name"] = "Alice";

ObjectAccessor<User> bound = GeneratedObjects.Create(user);
Console.WriteLine(bound["Name"]);
if (bound.TryGetValue("Id", out var id))
    Console.WriteLine(id);

if (accessor.CreateNewSupported)
    user = accessor.CreateNew();

public sealed class User
{
    [Ordinal(0)]
    public int Id { get; set; }
    [Ordinal(1)]
    public string? Name { get; set; }
}
```

- `ObjectAccessor<T>.Source` 非空且只读。访问依据编译期的 `T`，不会根据对象的运行时派生类型发现更多成员。
- `GetMembers()` 返回按名称 `StringComparer.Ordinal` 排序的只读描述列表；`GetMemberDictionary()` 返回区分大小写的只读字典。
- `Member` 提供 `Name`、`Type`、`Ordinal`、`CanRead`、`CanWrite`，并包含生成器使用的声明身份及读取器有效类型/空值信息；不包含 `MemberInfo` 或任意特性实例。
- `object?` 值会装箱/解箱，不进行任意类型转换。
- `CreateNew` 仅使用合法的 **公开无参构造**。抽象类、仅有非公开构造、未满足必需成员约束时 `CreateNewSupported=false`，调用 `CreateNew` 抛 `NotSupportedException`。

### 支持边界

支持可命名、对生成文件可访问的普通类/记录类、嵌套类、封闭泛型类。根类型不支持结构体、接口、`object`、动态类型/动态语言运行时、开放类型参数、匿名类型/文件局部类型或不可访问类型。

只包含公开实例属性与字段，排除静态成员、常量、索引器、非公开成员。属性的获取器/设置器分别检查是否公开；只读字段与仅可初始化属性不可写。不访问后备字段。

派生层的合格声明优先；派生的私有/静态同名声明不遮掉可访问基类成员。属性仍遵循虚调用分派。引用返回、类引用类型、指针等无法安全用 `object` 表达的目标成员会触发编译诊断，不静默忽略。

### 异常约定

| 情形 | 结果 |
|---|---|
| 目标、源或成员名为空 | `ArgumentNullException` |
| 索引器未知名称/不可读/不可写 | `ArgumentOutOfRangeException`，参数 `name` |
| 尝试操作的名称未知/不可执行 | `false`；`TryGet` 输出 `null` |
| 用户获取器/设置器/构造异常 | 原样传播 |
| 值类型错误/不合法解箱 | 原样传播，不由尝试操作吞掉 |
| 没有生成并注册访问器 | 带类型与修复建议的 `InvalidOperationException` |
| 注册元数据/契约版本冲突 | `InvalidOperationException`，不静默接受首个不同实现 |

## 同步与异步读取器

```csharp
using HKW.FastMember.Generater;

using var reader = ObjectReader.Create(users, "Id", "Name");
while (reader.Read())
    Console.WriteLine(reader.GetInt32(0));

await using var asyncReader = ObjectReader.Create(asyncUsers, cancellationToken, "Id", "Name");
while (await asyncReader.ReadAsync(cancellationToken))
    Console.WriteLine(asyncReader.IsDBNull(1) ? "<null>" : asyncReader.GetString(1));
```

`users` 为 `IEnumerable<User>`，`asyncUsers` 为 `IAsyncEnumerable<User>`。也提供不带创建取消令牌的异步源重载。即使只调用读取器，也会生成完整类型访问器。

### 列与架构

- 不传列或传空数组：只选可读成员，按 `Ordinal` 数值、再按名称序号比较排序；未标记序号默认 `-1`。
- 显式列保持顺序，允许重复；`GetOrdinal` 区分大小写并返回首次位置。未知列名抛 `IndexOutOfRangeException`。
- 列数组或名称为空时报 `ArgumentNullException`；显式未知/不可读列在创建时抛 `ArgumentOutOfRangeException`，不会先取得枚举器。
- 创建读取器和查询架构不枚举源。空源仍可查询完整架构。
- 空值映射 `DBNull.Value`；`Nullable<T>` 的 `GetFieldType` 为 `T`，`AllowDBNull=true`。非可空值类型为 `false`；引用类型为 `true`（不根据可空引用类型注解改变）。
- `GetSchemaTable` / `GetColumnSchema` 的列名、序号、类型与空值标志一致，`IsKey=false`。不执行任意类型转换。

### 生命周期、取消与释放

- `HasRows` 准确判断空源，必要时预读并缓存首行；随后的 `Read`/`ReadAsync` 不跳过该行。非空结果读到末尾后仍有 `HasRows=true`；结束唯一结果集后为 `false`。
- 读取前/结束后取值、空行对象，以及关闭后的数据/架构/读取操作抛 `InvalidOperationException`。到达末尾后重复读取为 `false`；`NextResult` 为 `false`。
- 异步源的同步 `Read`、`HasRows`、`Close`/`Dispose` **可能阻塞**。建议使用 `ReadAsync`/`DisposeAsync`；本库使用 `ConfigureAwait(false)` 也无法消除用户枚举器捕获同步上下文所导致的死锁风险。
- 创建取消令牌在创建时检查，并传给延迟取得的异步枚举器。每次 `ReadAsync(token)` 在读取前检查该调用的取消令牌；取消不消费已缓存的行。
- 每次读取传入的取消令牌 **不能保证取消由另一取消令牌创建且已运行的 `MoveNextAsync`**；不会为此重建枚举器或放弃仍在执行的操作。
- 不支持并行读取或与 `Dispose` 并发。重叠读取会被拒绝。
- `Close`/`Dispose`/`DisposeAsync` 共用一次性释放所有权，重复调用不会再次释放枚举器；未开始枚举就释放，不会为了释放而创建枚举器。
- 枚举异常原样传播，当前行失效；异常之后不继续读取，清理不覆盖原始枚举异常。

## 单元测试

在仓库根目录使用现有 MSTest/VSTest 运行方式：

```powershell
dotnet test .\HKW.FastMember.Generater.Tests\HKW.FastMember.Generater.Tests.csproj -c Release -p:GeneratePackageOnBuild=false
```

默认运行 net8.0 与 net10.0。测试覆盖生成代码、诊断、访问器、注册、同步/异步读取器工厂及双后端一致性；只有测试项目为一致性验证引用反射后端。

