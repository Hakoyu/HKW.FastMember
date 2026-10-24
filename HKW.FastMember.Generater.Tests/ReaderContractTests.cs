using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Member = HKW.FastMember.Abstractions.Member;

namespace HKW.FastMember.Generater.Tests;

[TestClass]
public sealed class ReaderContractTests
{
    [TestMethod]
    public void DataTableLoadAcceptsGeneratedSchemaAndDBNull()
    {
        using var reader = ObjectReader.Create(
            new[]
            {
                new Row { Id = 1 },
                new Row
                {
                    Id = 2,
                    Name = "two",
                    Count = 3,
                },
            },
            "Id",
            "Name",
            "Count"
        );
        using var table = new DataTable();
        table.Load(reader);
        Assert.AreEqual(2, table.Rows.Count);
        Assert.AreEqual(typeof(int), table.Columns["Count"]!.DataType);
        Assert.IsTrue(table.Columns["Count"]!.AllowDBNull);
        Assert.AreEqual(DBNull.Value, table.Rows[0]["Name"]);
        Assert.AreEqual(3, table.Rows[1]["Count"]);
    }

    [TestMethod]
    public async Task AsyncSchemaAndValuesUseTheSameGeneratedMetadata()
    {
        await using var reader = ObjectReader.Create(new[] { new Row { Id = 8 } });
        var columns = await reader.GetColumnSchemaAsync();
        Assert.AreEqual(reader.FieldCount, columns.Count);
        Assert.IsTrue(await reader.ReadAsync());
        Assert.AreEqual(8, await reader.GetFieldValueAsync<int>(reader.GetOrdinal("Id")));
        Assert.IsTrue(await reader.IsDBNullAsync(reader.GetOrdinal("Name")));
        using var table = await reader.GetSchemaTableAsync();
        for (var i = 0; i < columns.Count; i++)
        {
            Assert.AreEqual(columns[i].ColumnName, table!.Rows[i][SchemaTableColumn.ColumnName]);
            Assert.AreEqual(columns[i].DataType, reader.GetFieldType(i));
            Assert.AreEqual(-1, columns[i].ColumnSize);
            Assert.AreEqual(columns[i].ColumnSize, table.Rows[i][SchemaTableColumn.ColumnSize]);
        }
    }

    [TestMethod]
    public void ReaderReturnAndMetadataKeepTheDamContract()
    {
        using var reader = ObjectReader.Create(Array.Empty<Row>());
        var required =
            DynamicallyAccessedMemberTypes.PublicFields
            | DynamicallyAccessedMemberTypes.PublicProperties;
        var returnAttribute = reader
            .GetType()
            .GetMethod(nameof(DbDataReader.GetFieldType))!
            .ReturnParameter.GetCustomAttribute<DynamicallyAccessedMembersAttribute>();
        Assert.AreEqual(required, returnAttribute!.MemberTypes);
        var metadataAttribute = typeof(Member)
            .GetProperty(nameof(Member.EffectiveType))!
            .GetCustomAttribute<DynamicallyAccessedMembersAttribute>();
        Assert.AreEqual(required, metadataAttribute!.MemberTypes);
        Assert.IsNull(typeof(ObjectReader).GetCustomAttribute<RequiresUnreferencedCodeAttribute>());
        Assert.IsFalse(
            typeof(TypeAccessor)
                .Assembly.GetReferencedAssemblies()
                .Any(a => a.Name == "HKW.FastMember")
        );
    }

    public sealed class Row
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public int? Count { get; set; }
    }
}
