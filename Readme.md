# HKW.FastMember

`HKW.FastMember` 是一个面向现代 .NET 的高性能成员访问库。当类型或成员名称只能在运行时确定时，它可以用接近直接访问的方式读取、写入对象的字段和属性，并可将对象序列包装为 ADO.NET `DbDataReader`。

## 原始参考项目

[fast-member](https://github.com/mgravell/fast-member)

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

成员不存在时，索引器会抛出异常。需要探测成员时可使用 Try API：

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

类型具有可用的无参构造函数时，可以通过访问器创建实例：

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

`allowNonPublicAccessors` 允许调用公开属性的非公开 getter 或 setter，但不会额外暴露私有字段或私有属性。

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

`ObjectReader` 继承自 `DbDataReader`，可以将对象序列直接加载到 `DataTable`：

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

未指定列时，将自动使用可访问成员。显式指定列时，列顺序与参数顺序一致。

### 自定义默认列顺序

使用 `OrdinalAttribute` 可以指定自动发现成员时的顺序：

```csharp
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
- Try API 对不存在或不支持的成员返回 `false`。
- 用户属性 getter 或 setter 抛出的异常会原样传播，不会被 Try API 吞掉。
- `ObjectReader` 中的 `null` 会以 `DBNull.Value` 返回。
- 动态对象无法自动枚举成员，创建 `ObjectReader` 时必须显式提供列名。

## AOT 与裁剪限制

本库的内置访问器依赖运行时 IL 生成或 DLR，**不支持 Native AOT**，也没有反射后备或源生成访问器。Unity IL2CPP 等受限环境不在支持范围内，且未经过验证。

- `TypeAccessor.Create`、`ObjectAccessor.Create` 和同步/异步 `ObjectReader.Create` 标注了 `RequiresDynamicCode` 与 `RequiresUnreferencedCode`。启用相关分析器时，调用方会看到 `IL3050`（动态代码）和 `IL2026`（裁剪）警告。
- 当 `RuntimeFeature.IsDynamicCodeSupported` 为 `false` 时，内置工厂会在创建访问器之前抛出带明确说明的 `PlatformNotSupportedException`，包括动态对象分支。不会尝试降级或开始枚举对象序列。`TypeAccessor` / `ObjectAccessor` 的空参数仍优先抛出 `ArgumentNullException`。
- 裁剪和 AOT 是不同限制：即使环境允许动态代码，裁剪仍可能移除按名称访问的成员、非公开访问器或后备字段。`TypeHelpers` 和访问器的元数据查询也标注裁剪限制，但纯元数据发现不因此要求动态代码。
- 普通未裁剪的 JIT 运行方式保持不变。将诊断警告视为错误的消费者可能在构建时发现这些既有限制；不应以忽略警告代替兼容性验证。

### 诊断验证项目

`HKW.FastMember.AotDiagnostics` 刻意保留直接工厂调用，用于验证消费者能收到上述警告。它不是 AOT 成员访问示例：JIT 下验证正常读写，Native AOT 下验证明确拒绝且不执行用户 getter 或枚举输入。项目没有默认启用 `PublishAot`，普通解决方案构建不要求原生工具链。

下面是 Windows x64 的验证命令；在其他系统上应使用对应 RID 和原生工具链。发布前需安装 [Native AOT 前置工具](https://learn.microsoft.com/dotnet/core/deploying/native-aot/#prerequisites)，Windows 需要 Visual Studio 的“使用 C++ 的桌面开发”工作负载。

```powershell
# 两个目标框架均验证；发布期间预期出现 IL3050/IL2026。
foreach ($framework in 'net8.0', 'net10.0') {
    dotnet run --project HKW.FastMember.AotDiagnostics -c Release -f $framework
    if ($LASTEXITCODE -ne 0) { throw 'JIT diagnostic verification failed' }
    dotnet publish HKW.FastMember.AotDiagnostics -c Release -f $framework -r win-x64 -p:PublishAot=true -p:TrimmerSingleWarn=false
    if ($LASTEXITCODE -ne 0) { throw 'Native AOT publish failed' }
    & ".\HKW.FastMember.AotDiagnostics\bin\Release\$framework\win-x64\publish\HKW.FastMember.AotDiagnostics.exe"
    if ($LASTEXITCODE -ne 0) { throw 'Native AOT diagnostic verification failed' }
}
```

成功时输出 `PASS`，原生程序应报告 `IsDynamicCodeSupported=False`。这只证明诊断与提前拒绝符合契约，**不代表库已支持 AOT**。

## 基准测试

基准项目使用 BenchmarkDotNet，包含以下对比：

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

```powershell
dotnet build HKW.FastMember.sln -c Release
dotnet test HKW.FastMemberTests/HKW.FastMemberTests.csproj -c Release
```

测试同时覆盖 `net8.0` 与 `net10.0`，包括普通成员、动态对象、同步与异步读取器、错误契约、并发缓存和 ADO.NET 兼容行为。

## 项目结构

| 项目 | 说明 |
|---|---|
| `HKW.FastMember` | 核心库 |
| `HKW.FastMemberTests` | MSTest 单元测试 |
| `HKW.FastMember.Benchmarks` | BenchmarkDotNet 性能基准 |
| `HKW.FastMember.AotDiagnostics` | JIT 正常行为与 Native AOT 提前拒绝的诊断验证 |

