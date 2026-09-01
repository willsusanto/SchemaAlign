using SchemaAlign.Models;
using SchemaAlign.Models.TypeMapping;
using Xunit;


namespace SchemaAlign.Tests.Models;

public class TypeMapperTests
{
    [Theory]
    // C# Keyword / Aliases
    [InlineData("int", StandardType.Int, false)]
    [InlineData("int?", StandardType.Int, true)]
    [InlineData("Nullable<int>", StandardType.Int, true)]
    [InlineData("System.Nullable<int>", StandardType.Int, true)]
    [InlineData("long", StandardType.BigInt, false)]
    [InlineData("long?", StandardType.BigInt, true)]
    [InlineData("short", StandardType.SmallInt, false)]
    [InlineData("short?", StandardType.SmallInt, true)]
    [InlineData("byte", StandardType.TinyInt, false)]
    [InlineData("byte?", StandardType.TinyInt, true)]
    [InlineData("sbyte", StandardType.SmallInt, false)]
    [InlineData("sbyte?", StandardType.SmallInt, true)]
    [InlineData("uint", StandardType.Int, false)]
    [InlineData("uint?", StandardType.Int, true)]
    [InlineData("ulong", StandardType.BigInt, false)]
    [InlineData("ulong?", StandardType.BigInt, true)]
    [InlineData("ushort", StandardType.SmallInt, false)]
    [InlineData("ushort?", StandardType.SmallInt, true)]
    [InlineData("string", StandardType.String, false)]
    [InlineData("string?", StandardType.String, true)]
    [InlineData("char", StandardType.String, false)]
    [InlineData("char?", StandardType.String, true)]
    [InlineData("bool", StandardType.Boolean, false)]
    [InlineData("bool?", StandardType.Boolean, true)]
    [InlineData("decimal", StandardType.Decimal, false)]
    [InlineData("decimal?", StandardType.Decimal, true)]
    [InlineData("double", StandardType.Double, false)]
    [InlineData("double?", StandardType.Double, true)]
    [InlineData("float", StandardType.Float, false)]
    [InlineData("float?", StandardType.Float, true)]
    [InlineData("byte[]", StandardType.ByteArray, false)]
    [InlineData("byte[]?", StandardType.ByteArray, true)]
    // CLR Type Names
    [InlineData("Int32", StandardType.Int, false)]
    [InlineData("Int32?", StandardType.Int, true)]
    [InlineData("Int64", StandardType.BigInt, false)]
    [InlineData("Int64?", StandardType.BigInt, true)]
    [InlineData("Int16", StandardType.SmallInt, false)]
    [InlineData("Int16?", StandardType.SmallInt, true)]
    [InlineData("Byte", StandardType.TinyInt, false)]
    [InlineData("Byte?", StandardType.TinyInt, true)]
    [InlineData("UInt32", StandardType.Int, false)]
    [InlineData("UInt64", StandardType.BigInt, false)]
    [InlineData("UInt16", StandardType.SmallInt, false)]
    [InlineData("SByte", StandardType.SmallInt, false)]
    [InlineData("String", StandardType.String, false)]
    [InlineData("String?", StandardType.String, true)]
    [InlineData("Boolean", StandardType.Boolean, false)]
    [InlineData("Boolean?", StandardType.Boolean, true)]
    [InlineData("Decimal", StandardType.Decimal, false)]
    [InlineData("Decimal?", StandardType.Decimal, true)]
    [InlineData("Double", StandardType.Double, false)]
    [InlineData("Double?", StandardType.Double, true)]
    [InlineData("Single", StandardType.Float, false)]
    [InlineData("Single?", StandardType.Float, true)]
    [InlineData("DateTime", StandardType.DateTime, false)]
    [InlineData("DateTime?", StandardType.DateTime, true)]
    [InlineData("DateTimeOffset", StandardType.DateTimeOffset, false)]
    [InlineData("DateTimeOffset?", StandardType.DateTimeOffset, true)]
    [InlineData("DateOnly", StandardType.Date, false)]
    [InlineData("DateOnly?", StandardType.Date, true)]
    [InlineData("TimeOnly", StandardType.Time, false)]
    [InlineData("TimeOnly?", StandardType.Time, true)]
    [InlineData("TimeSpan", StandardType.Time, false)]
    [InlineData("TimeSpan?", StandardType.Time, true)]
    [InlineData("Guid", StandardType.Guid, false)]
    [InlineData("Guid?", StandardType.Guid, true)]
    [InlineData("Byte[]", StandardType.ByteArray, false)]
    [InlineData("Byte[]?", StandardType.ByteArray, true)]
    // Fully-Qualified System Names
    [InlineData("System.Int32", StandardType.Int, false)]
    [InlineData("System.Int64", StandardType.BigInt, false)]
    [InlineData("System.Int16", StandardType.SmallInt, false)]
    [InlineData("System.Byte", StandardType.TinyInt, false)]
    [InlineData("System.String", StandardType.String, false)]
    [InlineData("System.Boolean", StandardType.Boolean, false)]
    [InlineData("System.Decimal", StandardType.Decimal, false)]
    [InlineData("System.Double", StandardType.Double, false)]
    [InlineData("System.Single", StandardType.Float, false)]
    [InlineData("System.DateTime", StandardType.DateTime, false)]
    [InlineData("System.DateTimeOffset", StandardType.DateTimeOffset, false)]
    [InlineData("System.DateOnly", StandardType.Date, false)]
    [InlineData("System.TimeOnly", StandardType.Time, false)]
    [InlineData("System.TimeSpan", StandardType.Time, false)]
    [InlineData("System.Guid", StandardType.Guid, false)]
    [InlineData("System.Byte[]", StandardType.ByteArray, false)]
    [InlineData("Nullable<System.Guid>", StandardType.Guid, true)]
    [InlineData("System.Nullable<System.Int32>", StandardType.Int, true)]
    // Unknown types
    [InlineData("CustomClass", StandardType.Unknown, false)]
    [InlineData("CustomClass?", StandardType.Unknown, true)]
    public void FromCSharpType_ShouldMapCorrectly(string csharpType, StandardType expectedType, bool expectedNullable)
    {
        var actualType = TypeMapper.FromCSharpType(csharpType, out var isNullable);

        actualType.Should().Be(expectedType);
        isNullable.Should().Be(expectedNullable);
    }

    [Theory]
    [InlineData(StandardType.Int, false, "int")]
    [InlineData(StandardType.Int, true, "int?")]
    [InlineData(StandardType.BigInt, false, "long")]
    [InlineData(StandardType.BigInt, true, "long?")]
    [InlineData(StandardType.SmallInt, false, "short")]
    [InlineData(StandardType.SmallInt, true, "short?")]
    [InlineData(StandardType.TinyInt, false, "byte")]
    [InlineData(StandardType.TinyInt, true, "byte?")]
    [InlineData(StandardType.String, false, "string")]
    [InlineData(StandardType.String, true, "string?")]
    [InlineData(StandardType.Boolean, false, "bool")]
    [InlineData(StandardType.Boolean, true, "bool?")]
    [InlineData(StandardType.Decimal, false, "decimal")]
    [InlineData(StandardType.Decimal, true, "decimal?")]
    [InlineData(StandardType.Double, false, "double")]
    [InlineData(StandardType.Double, true, "double?")]
    [InlineData(StandardType.Float, false, "float")]
    [InlineData(StandardType.Float, true, "float?")]
    [InlineData(StandardType.DateTime, false, "DateTime")]
    [InlineData(StandardType.DateTime, true, "DateTime?")]
    [InlineData(StandardType.DateTimeOffset, false, "DateTimeOffset")]
    [InlineData(StandardType.DateTimeOffset, true, "DateTimeOffset?")]
    [InlineData(StandardType.Date, false, "DateOnly")]
    [InlineData(StandardType.Date, true, "DateOnly?")]
    [InlineData(StandardType.Time, false, "TimeOnly")]
    [InlineData(StandardType.Time, true, "TimeOnly?")]
    [InlineData(StandardType.Guid, false, "Guid")]
    [InlineData(StandardType.Guid, true, "Guid?")]
    [InlineData(StandardType.ByteArray, false, "byte[]")]
    [InlineData(StandardType.ByteArray, true, "byte[]?")]
    [InlineData(StandardType.Json, false, "string")]
    [InlineData(StandardType.Json, true, "string?")]
    [InlineData(StandardType.Unknown, false, "string")]
    [InlineData(StandardType.Unknown, true, "string?")]
    public void ToCSharpType_ShouldMapCorrectly(StandardType type, bool isNullable, string expectedCSharpType)
    {
        var actual = TypeMapper.ToCSharpType(type, isNullable);
        actual.Should().Be(expectedCSharpType);
    }

    [Theory]
    // String types
    [InlineData("nvarchar", 50, null, null, StandardType.String, 50, null, null)]
    [InlineData("nvarchar(50)", null, null, null, StandardType.String, 50, null, null)]
    [InlineData("varchar", 100, null, null, StandardType.String, 100, null, null)]
    [InlineData("varchar(100)", null, null, null, StandardType.String, 100, null, null)]
    [InlineData("nvarchar", -1, null, null, StandardType.String, -1, null, null)]
    [InlineData("nvarchar(max)", null, null, null, StandardType.String, -1, null, null)]
    [InlineData("varchar(max)", null, null, null, StandardType.String, -1, null, null)]
    [InlineData("nchar(10)", null, null, null, StandardType.String, 10, null, null)]
    [InlineData("char(20)", null, null, null, StandardType.String, 20, null, null)]
    [InlineData("text", null, null, null, StandardType.String, null, null, null)]
    [InlineData("ntext", null, null, null, StandardType.String, null, null, null)]
    [InlineData("sysname", null, null, null, StandardType.String, 128, null, null)]
    [InlineData("xml", null, null, null, StandardType.String, null, null, null)]
    [InlineData("json", null, null, null, StandardType.Json, null, null, null)]
    // Exact Numerics
    [InlineData("int", null, null, null, StandardType.Int, null, null, null)]
    [InlineData("bigint", null, null, null, StandardType.BigInt, null, null, null)]
    [InlineData("smallint", null, null, null, StandardType.SmallInt, null, null, null)]
    [InlineData("tinyint", null, null, null, StandardType.TinyInt, null, null, null)]
    [InlineData("bit", null, null, null, StandardType.Boolean, null, null, null)]
    [InlineData("decimal", null, 18, 2, StandardType.Decimal, null, 18, 2)]
    [InlineData("decimal(18, 4)", null, null, null, StandardType.Decimal, null, 18, 4)]
    [InlineData("decimal(18,4)", null, null, null, StandardType.Decimal, null, 18, 4)]
    [InlineData("decimal(10)", null, null, null, StandardType.Decimal, null, 10, null)]
    [InlineData("numeric", null, 10, 4, StandardType.Decimal, null, 10, 4)]
    [InlineData("numeric(12, 3)", null, null, null, StandardType.Decimal, null, 12, 3)]
    [InlineData("money", null, null, null, StandardType.Decimal, null, null, null)]
    [InlineData("smallmoney", null, null, null, StandardType.Decimal, null, null, null)]
    // Approximate Numerics
    [InlineData("float", null, null, null, StandardType.Double, null, null, null)]
    [InlineData("float(53)", null, null, null, StandardType.Double, null, null, null)]
    [InlineData("float(24)", null, null, null, StandardType.Float, null, null, null)]
    [InlineData("real", null, null, null, StandardType.Float, null, null, null)]
    // Date & Time
    [InlineData("datetime2", null, null, null, StandardType.DateTime, null, null, null)]
    [InlineData("datetime2(7)", null, null, null, StandardType.DateTime, null, 7, null)]
    [InlineData("datetime2(3)", null, null, null, StandardType.DateTime, null, 3, null)]
    [InlineData("datetime", null, null, null, StandardType.DateTime, null, null, null)]
    [InlineData("smalldatetime", null, null, null, StandardType.DateTime, null, null, null)]
    [InlineData("datetimeoffset", null, null, null, StandardType.DateTimeOffset, null, null, null)]
    [InlineData("datetimeoffset(7)", null, null, null, StandardType.DateTimeOffset, null, 7, null)]
    [InlineData("date", null, null, null, StandardType.Date, null, null, null)]
    [InlineData("time", null, null, null, StandardType.Time, null, null, null)]
    [InlineData("time(7)", null, null, null, StandardType.Time, null, 7, null)]
    // Guid
    [InlineData("uniqueidentifier", null, null, null, StandardType.Guid, null, null, null)]
    // Binary
    [InlineData("varbinary", 256, null, null, StandardType.ByteArray, 256, null, null)]
    [InlineData("varbinary(256)", null, null, null, StandardType.ByteArray, 256, null, null)]
    [InlineData("varbinary(max)", null, null, null, StandardType.ByteArray, -1, null, null)]
    [InlineData("binary(16)", null, null, null, StandardType.ByteArray, 16, null, null)]
    [InlineData("image", null, null, null, StandardType.ByteArray, null, null, null)]
    [InlineData("rowversion", null, null, null, StandardType.ByteArray, null, null, null)]
    [InlineData("timestamp", null, null, null, StandardType.ByteArray, null, null, null)]
    public void FromSqlServerType_ShouldMapCorrectly(
        string sqlType, int? inLength, int? inPrecision, int? inScale,
        StandardType expectedType, int? expectedLength, int? expectedPrecision, int? expectedScale)
    {
        var actual = TypeMapper.FromSqlServerType(sqlType, inLength, inPrecision, inScale, out var length, out var prec, out var scale);

        actual.Should().Be(expectedType);
        length.Should().Be(expectedLength);
        prec.Should().Be(expectedPrecision);
        scale.Should().Be(expectedScale);
    }

    [Theory]
    [InlineData(StandardType.String, 50, null, null, "NVARCHAR(50)")]
    [InlineData(StandardType.String, -1, null, null, "NVARCHAR(MAX)")]
    [InlineData(StandardType.String, null, null, null, "NVARCHAR(MAX)")]
    [InlineData(StandardType.Json, null, null, null, "NVARCHAR(MAX)")]
    [InlineData(StandardType.Json, 4000, null, null, "NVARCHAR(4000)")]
    [InlineData(StandardType.Int, null, null, null, "INT")]
    [InlineData(StandardType.BigInt, null, null, null, "BIGINT")]
    [InlineData(StandardType.SmallInt, null, null, null, "SMALLINT")]
    [InlineData(StandardType.TinyInt, null, null, null, "TINYINT")]
    [InlineData(StandardType.Boolean, null, null, null, "BIT")]
    [InlineData(StandardType.Decimal, null, 18, 4, "DECIMAL(18,4)")]
    [InlineData(StandardType.Decimal, null, 10, 0, "DECIMAL(10,0)")]
    [InlineData(StandardType.Decimal, null, 10, null, "DECIMAL(10)")]
    [InlineData(StandardType.Decimal, null, null, null, "DECIMAL(18,2)")]
    [InlineData(StandardType.Double, null, null, null, "FLOAT")]
    [InlineData(StandardType.Float, null, null, null, "REAL")]
    [InlineData(StandardType.DateTime, null, null, null, "DATETIME2")]
    [InlineData(StandardType.DateTimeOffset, null, null, null, "DATETIMEOFFSET")]
    [InlineData(StandardType.Date, null, null, null, "DATE")]
    [InlineData(StandardType.Time, null, null, null, "TIME")]
    [InlineData(StandardType.Guid, null, null, null, "UNIQUEIDENTIFIER")]
    [InlineData(StandardType.ByteArray, 500, null, null, "VARBINARY(500)")]
    [InlineData(StandardType.ByteArray, -1, null, null, "VARBINARY(MAX)")]
    [InlineData(StandardType.ByteArray, null, null, null, "VARBINARY(MAX)")]
    public void ToSqlServerType_ShouldGenerateCorrectSqlType(
        StandardType type, int? length, int? precision, int? scale, string expectedSql)
    {
        var col = new ColumnSchema
        {
            Name = "Col1",
            Type = type,
            Length = length,
            Precision = precision,
            Scale = scale
        };

        var actualSql = TypeMapper.ToSqlServerType(col);
        actualSql.Should().Be(expectedSql);
    }

    [Theory]
    [InlineData("string", StandardType.String, null)]
    [InlineData("varchar(50)", StandardType.String, 50)]
    [InlineData("nvarchar(100)", StandardType.String, 100)]
    [InlineData("text", StandardType.String, null)]
    [InlineData("xml", StandardType.String, null)]
    [InlineData("json", StandardType.Json, null)]
    [InlineData("jsonb", StandardType.Json, null)]
    [InlineData("int", StandardType.Int, null)]
    [InlineData("integer", StandardType.Int, null)]
    [InlineData("int4", StandardType.Int, null)]
    [InlineData("serial", StandardType.Int, null)]
    [InlineData("bigint", StandardType.BigInt, null)]
    [InlineData("int8", StandardType.BigInt, null)]
    [InlineData("long", StandardType.BigInt, null)]
    [InlineData("smallint", StandardType.SmallInt, null)]
    [InlineData("int2", StandardType.SmallInt, null)]
    [InlineData("short", StandardType.SmallInt, null)]
    [InlineData("tinyint", StandardType.TinyInt, null)]
    [InlineData("byte", StandardType.TinyInt, null)]
    [InlineData("boolean", StandardType.Boolean, null)]
    [InlineData("bool", StandardType.Boolean, null)]
    [InlineData("bit", StandardType.Boolean, null)]
    [InlineData("decimal", StandardType.Decimal, null)]
    [InlineData("numeric", StandardType.Decimal, null)]
    [InlineData("money", StandardType.Decimal, null)]
    [InlineData("currency", StandardType.Decimal, null)]
    [InlineData("float", StandardType.Float, null)]
    [InlineData("real", StandardType.Float, null)]
    [InlineData("float4", StandardType.Float, null)]
    [InlineData("double", StandardType.Double, null)]
    [InlineData("float8", StandardType.Double, null)]
    [InlineData("datetime", StandardType.DateTime, null)]
    [InlineData("datetime2", StandardType.DateTime, null)]
    [InlineData("timestamp", StandardType.DateTime, null)]
    [InlineData("datetimeoffset", StandardType.DateTimeOffset, null)]
    [InlineData("timestamptz", StandardType.DateTimeOffset, null)]
    [InlineData("date", StandardType.Date, null)]
    [InlineData("time", StandardType.Time, null)]
    [InlineData("guid", StandardType.Guid, null)]
    [InlineData("uuid", StandardType.Guid, null)]
    [InlineData("uniqueidentifier", StandardType.Guid, null)]
    [InlineData("blob", StandardType.ByteArray, null)]
    [InlineData("bytea", StandardType.ByteArray, null)]
    [InlineData("byte[]", StandardType.ByteArray, null)]
    [InlineData("binary(256)", StandardType.ByteArray, 256)]
    public void FromMermaidType_ShouldMapCorrectly(string mermaidType, StandardType expectedType, int? expectedLength)
    {
        var actual = TypeMapper.FromMermaidType(mermaidType, out var length);

        actual.Should().Be(expectedType);
        length.Should().Be(expectedLength);
    }
}

