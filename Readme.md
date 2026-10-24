# HKW.FastMember

`HKW.FastMember` 是一个面向现代 .NET 的高性能成员访问库。当类型或成员名称只能在运行时确定时，它可以用接近直接访问的方式读取、写入对象的字段和属性，并可将对象序列包装为 ADO.NET `DbDataReader`。


## 原始参考项目

[fast-member](https://github.com/mgravell/fast-member)

## 共享抽象与两个后端

| 包 | 职责 |
|---|---|
| [HKW.FastMember.Abstractions](HKW.FastMember.Abstractions/Readme.md) | 公共泛型/非泛型访问器、纯成员描述 `Member`、唯一序号特性 `OrdinalAttribute`、同步/异步读取器 |
| HKW.FastMember | 反射发现、Emit/DLR 访问核心及独立工厂/缓存 |
| [HKW.FastMember.Generater](HKW.FastMember.Generater/Readme.md) | 编译期生成访问核心及独立工厂/注册；同包包含分析器 |

两个后端各自正常依赖 `HKW.FastMember.Abstractions`，互不引用；公共层不选择后端。没有自动回退或全局后端解析。源生成后端没有 Emit、DLR、反射访问或 `Expression.Compile` 后备。两个后端返回相同的公共类型，读取器只有一套实现。

```csharp
using HKW.FastMember.Abstractions;
using ReflectionTypes = HKW.FastMember.TypeAccessor;
using GeneratedTypes = HKW.FastMember.Generater.TypeAccessor;

TypeAccessor<User> reflected = ReflectionTypes.Create<User>();
TypeAccessor<User> generated = GeneratedTypes.Create<User>();
using var reader = ObjectReader.Create(users, generated, "Id", "Name");
```

## 功能特性

- 通过名称高速读写字段和属性
- 支持普通对象、结构体、匿名类型和 DLR 动态对象
- 支持 `TryGetValue`、`TrySetValue` 无异常探测
- 支持只读、只写、私有访问器和引用返回属性
- 自动缓存 `TypeAccessor`，并支持并发创建和访问
- 提供成员元数据、特性和序号信息
- 将同步或异步对象序列包装为 `ObjectReader`
- 可直接用于 `DataTable.Load`、批量写入等 ADO.NET 场景
- 支持 `net8.0` 和 `net10.0`

## 安装

通过 NuGet 安装：

```powershell
dotnet add package HKW.FastMember
```

然后引入命名空间：

```csharp
using HKW.FastMember;
```

## 快速开始

### 使用 `TypeAccessor`

当需要对同一类型的多个实例反复访问时，优先使用 `TypeAccessor`。访问器会按类型缓存。

```csharp
public sealed class User
{
    public int Id { get; set; }
    public string? Name { get; set; }
}

var accessor = TypeAccessor.Create(typeof(User));
var user = new User();

accessor[user, nameof(User.Id)] = 1;
accessor[user, nameof(User.Name)] = "HKW";

var id = (int)accessor[user, nameof(User.Id)];
var name = (string?)accessor[user, nameof(User.Name)];
```

成员不存在时，索引器会抛出异常。需要探测成员时可使用 `TryGetValue`、`TrySetValue` 方法：

```csharp
if (accessor.TryGetValue(user, "Name", out var value))
{
    Console.WriteLine(value);
}

if (!accessor.TrySetValue(user, "Missing", 42))
{
    Console.WriteLine("成员不存在或不可写");
}
```

### 创建对象

类型具有可用的公开无参构造且满足必需成员（`required`）约束时，可以通过访问器创建实例；抽象类型或不能合法初始化的类型不支持：

```csharp
var accessor = TypeAccessor.Create(typeof(User));

if (accessor.CreateNewSupported)
{
    var user = (User)accessor.CreateNew();
}
```

### 使用 `ObjectAccessor`

只访问单个对象时，可以使用更简洁的 `ObjectAccessor`：

```csharp
var user = new User { Id = 1, Name = "Before" };
var accessor = ObjectAccessor.Create(user);

Console.WriteLine(accessor["Name"]);
accessor["Name"] = "After";
```

`ObjectAccessor` 同样支持动态对象：

```csharp
using System.Dynamic;

var value = new ExpandoObject();
var accessor = ObjectAccessor.Create(value);

accessor["Name"] = "Dynamic value";
Console.WriteLine(accessor["Name"]);
```

### 访问非公开访问器

`allowNonPublicAccessors` 允许调用公开属性的非公开取值或赋值访问器，但不会额外暴露私有字段或私有属性。

```csharp
public sealed class Entity
{
    public int Id { get; private set; }
}

var entity = new Entity();
var accessor = ObjectAccessor.Create(entity, allowNonPublicAccessors: true);
accessor["Id"] = 10;
```

## 成员元数据

可以查询访问器暴露的成员：

```csharp
var accessor = TypeAccessor.Create(typeof(User));

foreach (var member in accessor.GetMembers())
{
    Console.WriteLine($"{member.Name}: {member.Type}");
}

var membersByName = accessor.GetMemberDictionary();
var nameMember = membersByName[nameof(User.Name)];
```

也可以直接使用 `TypeHelpers` 并指定反射标志：

```csharp
using System.Reflection;

var members = TypeHelpers.GetMembers(
    typeof(User),
    BindingFlags.Public | BindingFlags.Instance
);
```

成员名称区分大小写。默认成员集合只包含公开实例字段和非索引属性，不包含静态成员与索引器。

## ObjectReader

### 同步对象序列

后端 `ObjectReader.Create` 返回公共层继承自 `DbDataReader` 的读取器，可以将对象序列直接加载到 `DataTable`：

```csharp
using System.Data;

var users = new[]
{
    new User { Id = 1, Name = "Alice" },
    new User { Id = 2, Name = "Bob" },
};

var table = new DataTable();
using var reader = ObjectReader.Create(users, nameof(User.Id), nameof(User.Name));
table.Load(reader);
```

未指定列时只选择可读成员，按 `Ordinal`、再按名称序号排序。显式列保留顺序及重复列，`GetOrdinal` 区分大小写并返回首次位置；未知/不可读列在创建时失败，列数组/名称为空时抛出 `ArgumentNullException`。创建读取器和获取架构时不枚举源，空源仍有完整架构。`Nullable<T>` 的列类型为 `T`，`AllowDBNull=true`；引用类型同样允许空值，`IsKey=false`，`ColumnSize=-1`。公共注入入口要求访问器目标类型能接收源的静态元素类型，不逐行发现派生类型。

### 自定义默认列顺序

使用公共层唯一的 `OrdinalAttribute` 可以指定默认列顺序：

```csharp
using HKW.FastMember.Abstractions;

public sealed class ExportRow
{
    [Ordinal(0)]
    public int Id { get; set; }

    [Ordinal(1)]
    public string? Name { get; set; }
}
```

显式传入列名时，显式顺序优先于 `OrdinalAttribute`。

### 异步对象序列

```csharp
static async IAsyncEnumerable<User> GetUsersAsync()
{
    yield return new User { Id = 1, Name = "Alice" };
    await Task.Yield();
    yield return new User { Id = 2, Name = "Bob" };
}

using var cancellation = new CancellationTokenSource();
await using var reader = ObjectReader.Create(
    GetUsersAsync(),
    cancellation.Token,
    nameof(User.Id),
    nameof(User.Name)
);

while (await reader.ReadAsync(cancellation.Token))
{
    Console.WriteLine($"{reader.GetInt32(0)}: {reader.GetString(1)}");
}
```

## 行为说明

- 成员名称使用区分大小写的序号比较。
- 索引器访问未知成员时会抛出 `ArgumentOutOfRangeException`。
- `TryGetValue`、`TrySetValue` 方法对不存在或不支持的成员返回 `false`。
- 用户属性的取值或赋值访问器抛出的异常会原样传播，不会被 `TryGetValue`、`TrySetValue` 方法吞掉。
- `ObjectReader` 中的 `null` 会以 `DBNull.Value` 返回。
- 动态对象没有固定成员表；访问器 `GetMembersSupported=false`，元数据 API 抛出 `NotSupportedException`。反射后端创建动态读取器必须显式提供列名，架构固定为 `object/AllowDBNull=true`；列可重复，空源仍有架构，缺失动态值时传播绑定错误而不伪造空值。DLR 索引器绑定失败抛出 `RuntimeBinderException`，`TryGetValue`、`TrySetValue` 返回 `false`，是独立于静态成员的契约。
- `HasRows` 可以预读缓存首行，不丢行；读取前/结束后取值、行对象为 `null`、关闭后数据操作抛出 `InvalidOperationException`。到达数据末尾后重复读取返回 `false`，只有一个结果集。
- 创建时的取消令牌传给异步枚举器；`ReadAsync(token)` 预检查该取消令牌，但不能保证取消使用另一取消令牌创建且正在运行的 `MoveNextAsync`。不会重新创建枚举器。
- `Close/Dispose/DisposeAsync` 至多释放一次；未开始读取不取得枚举器，枚举异常保持原异常，之后不继续枚举。
- 异步源的同步 `Read/HasRows/Close` 可能阻塞，优先用异步 API。重叠读取被拒绝；不支持读取与释放并发。

## AOT 与裁剪限制

**反射后端 HKW.FastMember** 的内置访问器依赖运行时 IL 生成或 DLR，**不支持 Native AOT**，不会自动切换为源生成后端。Unity IL2CPP 等受限环境不在支持范围内，且未经过验证。公共层和源生成工厂不继承反射后端的 RUC/RDC 限制；这不等于已经验证 NativeAOT 发布。

- `TypeAccessor.Create`、`ObjectAccessor.Create` 和同步/异步 `ObjectReader.Create` 标注了 `RequiresDynamicCode` 与 `RequiresUnreferencedCode`。启用相关分析器时，调用方会看到 `IL3050`（动态代码）和 `IL2026`（裁剪）警告。
- 当 `RuntimeFeature.IsDynamicCodeSupported` 为 `false` 时，内置工厂会在创建访问器之前抛出带明确说明的 `PlatformNotSupportedException`，包括动态对象分支。不会尝试降级或开始枚举对象序列。`TypeAccessor` / `ObjectAccessor` 的空参数仍优先抛出 `ArgumentNullException`。
- 裁剪和 AOT 是不同限制：即使环境允许动态代码，裁剪仍可能移除按名称访问的成员或非公开访问器。限制保留在反射创建、`TypeHelpers` 和反射特性查询边界；公共访问器读取既有纯描述不执行反射发现，不带反射后端的整体限制。
- 普通未裁剪的 JIT 运行方式保持不变。将诊断警告视为错误的消费者可能在构建时发现这些既有限制；不应以忽略警告代替兼容性验证。

## 基准测试

基准项目本轮仅迁移引用，**未编译、未运行**，不以旧基准推断重构后的性能。项目使用 BenchmarkDotNet，包含以下对比：

- 直接属性访问
- `TypeAccessor`
- `ObjectAccessor`
- DLR 动态访问
- 反射访问
- 编译表达式
- `ObjectReader`
- 并发访问

运行全部基准：

```powershell
dotnet run -c Release --project HKW.FastMember.Benchmarks -f net10.0
```

每个基准套件都在 `HKW.FastMember.Benchmarks/Program.cs` 中独立调用。只需注释对应的 `BenchmarkRunner.Run<T>()`，即可跳过不需要的项目。

## 构建与测试

仅测试范围的验证命令（不触发自动打包，不构建基准测试与 AOT 诊断项目）：

```powershell
dotnet test .\HKW.FastMember.Abstractions.Tests\HKW.FastMember.Abstractions.Tests.csproj -c Release -p:GeneratePackageOnBuild=false
dotnet test .\HKW.FastMemberTests\HKW.FastMemberTests.csproj -c Release -p:GeneratePackageOnBuild=false
dotnet test .\HKW.FastMember.Generater.Tests\HKW.FastMember.Generater.Tests.csproj -c Release -p:GeneratePackageOnBuild=false
```

三个测试项目均覆盖 `net8.0` 与 `net10.0`：公共层用人工访问器独立验证契约与读取器；反射后端验证 Emit/DLR 扩展；生成后端验证生成诊断及输出编译/执行，并通过仅测试项目的双后端引用验证一致性。

## 项目结构

| 项目 | 说明 |
|---|---|
| `HKW.FastMember.Abstractions` | 公共契约、元数据与唯一读取器实现 |
| `HKW.FastMember.Abstractions.Tests` | 不引用后端的 MSTest 契约与读取器测试 |
| `HKW.FastMember` | 反射发现、Emit/DLR 后端 |
| `HKW.FastMember.Tests` | 反射后端 MSTest 回归 |
| `HKW.FastMember.Generater` | 源生成后端与单包分析器布局 |
| `HKW.FastMember.Generater.SourceGenerator` | Roslyn 生成器/分析器，不单独发布 |
| `HKW.FastMember.Generater.Tests` | GeneratorDriver、生成行为及双后端一致性测试 |
| `HKW.FastMember.Benchmarks` | BenchmarkDotNet 性能基准 |
| `HKW.FastMember.AotDiagnostics` | JIT 正常行为与 Native AOT 提前拒绝的诊断验证 |

