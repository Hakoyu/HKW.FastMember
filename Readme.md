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

## AOT 限制

本库会在运行时生成 IL，因此不适用于禁止动态代码生成的受限 AOT 环境，例如 Unity IL2CPP。Native AOT 场景也应在采用前进行兼容性验证。

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

