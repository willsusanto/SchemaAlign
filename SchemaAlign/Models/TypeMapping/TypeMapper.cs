using System.Text.RegularExpressions;

namespace SchemaAlign.Models.TypeMapping;

public static class TypeMapper
{
    private static readonly Regex SqlDimensionRegex = new(@"^(\w+)(?:\((max|\d+)(?:,\s*(\d+))?\))?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static StandardType FromCSharpType(string csharpType, out bool isNullable)
    {
        csharpType = csharpType.Trim();
        isNullable = false;

        if (csharpType.EndsWith("?"))
        {
            isNullable = true;
            csharpType = csharpType.Substring(0, csharpType.Length - 1).Trim();
        }
        else if (csharpType.StartsWith("System.Nullable<", StringComparison.OrdinalIgnoreCase) && csharpType.EndsWith(">"))
        {
            isNullable = true;
            csharpType = csharpType.Substring(16, csharpType.Length - 17).Trim();
        }
        else if (csharpType.StartsWith("Nullable<", StringComparison.OrdinalIgnoreCase) && csharpType.EndsWith(">"))
        {
            isNullable = true;
            csharpType = csharpType.Substring(9, csharpType.Length - 10).Trim();
        }

        if (csharpType.StartsWith("System.", StringComparison.OrdinalIgnoreCase))
        {
            csharpType = csharpType.Substring(7).Trim();
        }

        return csharpType.ToLowerInvariant() switch
        {
            "int" or "int32" or "uint" or "uint32" => StandardType.Int,
            "long" or "int64" or "ulong" or "uint64" => StandardType.BigInt,
            "short" or "int16" or "ushort" or "uint16" or "sbyte" => StandardType.SmallInt,
            "byte" => StandardType.TinyInt,
            "string" or "char" => StandardType.String,
            "bool" or "boolean" => StandardType.Boolean,
            "decimal" => StandardType.Decimal,
            "double" => StandardType.Double,
            "float" or "single" => StandardType.Float,
            "datetime" => StandardType.DateTime,
            "datetimeoffset" => StandardType.DateTimeOffset,
            "dateonly" => StandardType.Date,
            "timeonly" or "timespan" => StandardType.Time,
            "guid" => StandardType.Guid,
            "byte[]" => StandardType.ByteArray,
            _ => StandardType.Unknown
        };
    }

    public static string ToCSharpType(StandardType type, bool isNullable)
    {
        var baseType = type switch
        {
            StandardType.Int => "int",
            StandardType.BigInt => "long",
            StandardType.SmallInt => "short",
            StandardType.TinyInt => "byte",
            StandardType.String => "string",
            StandardType.Boolean => "bool",
            StandardType.Decimal => "decimal",
            StandardType.Double => "double",
            StandardType.Float => "float",
            StandardType.DateTime => "DateTime",
            StandardType.DateTimeOffset => "DateTimeOffset",
            StandardType.Date => "DateOnly",
            StandardType.Time => "TimeOnly",
            StandardType.Guid => "Guid",
            StandardType.ByteArray => "byte[]",
            StandardType.Json => "string",
            _ => "string"
        };

        if (isNullable)
        {
            return baseType + "?";
        }

        return baseType;
    }

    public static StandardType FromSqlServerType(
        string sqlType, int? inLength, int? inPrecision, int? inScale,
        out int? length, out int? precision, out int? scale)
    {
        length = inLength;
        precision = inPrecision;
        scale = inScale;

        var cleanType = sqlType.Trim().ToLowerInvariant();
        int? extractedFirstParam = null;
        int? extractedSecondParam = null;
        bool isMax = false;

        var match = SqlDimensionRegex.Match(cleanType);
        if (match.Success)
        {
            cleanType = match.Groups[1].Value.ToLowerInvariant();
            if (match.Groups[2].Success)
            {
                var p1Str = match.Groups[2].Value;
                if (p1Str.Equals("max", StringComparison.OrdinalIgnoreCase))
                {
                    isMax = true;
                    extractedFirstParam = -1;
                }
                else
                {
                    extractedFirstParam = int.Parse(p1Str);
                }
            }
            if (match.Groups[3].Success)
            {
                extractedSecondParam = int.Parse(match.Groups[3].Value);
            }
        }

        switch (cleanType)
        {
            case "int":
                return StandardType.Int;
            case "bigint":
                return StandardType.BigInt;
            case "smallint":
                return StandardType.SmallInt;
            case "tinyint":
                return StandardType.TinyInt;
            case "bit":
                return StandardType.Boolean;
            case "decimal" or "numeric":
                if (!precision.HasValue) precision = extractedFirstParam;
                if (!scale.HasValue) scale = extractedSecondParam;
                return StandardType.Decimal;
            case "money" or "smallmoney":
                return StandardType.Decimal;
            case "float":
                if (extractedFirstParam.HasValue && extractedFirstParam.Value <= 24)
                {
                    return StandardType.Float;
                }
                return StandardType.Double;
            case "real":
                return StandardType.Float;
            case "datetime" or "smalldatetime":
                return StandardType.DateTime;
            case "datetime2":
                if (!precision.HasValue) precision = extractedFirstParam;
                return StandardType.DateTime;
            case "datetimeoffset":
                if (!precision.HasValue) precision = extractedFirstParam;
                return StandardType.DateTimeOffset;
            case "date":
                return StandardType.Date;
            case "time":
                if (!precision.HasValue) precision = extractedFirstParam;
                return StandardType.Time;
            case "uniqueidentifier":
                return StandardType.Guid;
            case "varbinary" or "binary":
                if (!length.HasValue) length = isMax ? -1 : extractedFirstParam;
                return StandardType.ByteArray;
            case "image" or "rowversion" or "timestamp":
                return StandardType.ByteArray;
            case "nvarchar" or "varchar" or "nchar" or "char":
                if (!length.HasValue) length = isMax ? -1 : extractedFirstParam;
                return StandardType.String;
            case "sysname":
                if (!length.HasValue) length = 128;
                return StandardType.String;
            case "text" or "ntext" or "xml":
                return StandardType.String;
            case "json":
                return StandardType.Json;
            default:
                return StandardType.Unknown;
        }
    }

    public static string ToSqlServerType(ColumnSchema column)
    {
        return column.Type switch
        {
            StandardType.Int => "INT",
            StandardType.BigInt => "BIGINT",
            StandardType.SmallInt => "SMALLINT",
            StandardType.TinyInt => "TINYINT",
            StandardType.Boolean => "BIT",
            StandardType.Decimal => column.Precision.HasValue && column.Scale.HasValue
                ? $"DECIMAL({column.Precision.Value},{column.Scale.Value})"
                : column.Precision.HasValue
                    ? $"DECIMAL({column.Precision.Value})"
                    : "DECIMAL(18,2)",
            StandardType.Double => "FLOAT",
            StandardType.Float => "REAL",
            StandardType.DateTime => "DATETIME2",
            StandardType.DateTimeOffset => "DATETIMEOFFSET",
            StandardType.Date => "DATE",
            StandardType.Time => "TIME",
            StandardType.Guid => "UNIQUEIDENTIFIER",
            StandardType.ByteArray => column.Length.HasValue && column.Length > 0
                ? $"VARBINARY({column.Length.Value})"
                : "VARBINARY(MAX)",
            StandardType.String or StandardType.Json => column.Length.HasValue && column.Length > 0
                ? $"NVARCHAR({column.Length.Value})"
                : "NVARCHAR(MAX)",
            _ => "NVARCHAR(MAX)"
        };
    }

    public static StandardType FromMermaidType(string mermaidType, out int? length)
    {
        return FromMermaidType(mermaidType, out length, out _, out _);
    }

    public static StandardType FromMermaidType(string mermaidType, out int? length, out int? precision, out int? scale)
    {
        length = null;
        precision = null;
        scale = null;
        var cleanType = mermaidType.Trim();
        var match = SqlDimensionRegex.Match(cleanType);
        if (match.Success)
        {
            cleanType = match.Groups[1].Value;
            if (match.Groups[2].Success)
            {
                var lenStr = match.Groups[2].Value;
                if (lenStr.Equals("max", StringComparison.OrdinalIgnoreCase))
                {
                    length = -1;
                }
                else
                {
                    var val = int.Parse(lenStr);
                    length = val;
                    precision = val;
                }
            }
            if (match.Groups[3].Success)
            {
                scale = int.Parse(match.Groups[3].Value);
            }
        }

        var stdType = cleanType.ToLowerInvariant() switch
        {
            "int" or "integer" or "int4" or "serial" or "number" => StandardType.Int,
            "bigint" or "int8" or "bigserial" or "long" => StandardType.BigInt,
            "smallint" or "int2" or "short" => StandardType.SmallInt,
            "tinyint" or "byte" => StandardType.TinyInt,
            "bool" or "boolean" or "bit" => StandardType.Boolean,
            "decimal" or "numeric" or "money" or "currency" => StandardType.Decimal,
            "float" or "real" or "float4" => StandardType.Float,
            "double" or "float8" => StandardType.Double,
            "datetime" or "datetime2" or "timestamp" => StandardType.DateTime,
            "datetimeoffset" or "timestamptz" => StandardType.DateTimeOffset,
            "date" => StandardType.Date,
            "time" or "timetz" => StandardType.Time,
            "guid" or "uuid" or "uniqueidentifier" => StandardType.Guid,
            "binary" or "varbinary" or "blob" or "bytea" or "byte[]" or "image" => StandardType.ByteArray,
            "string" or "varchar" or "nvarchar" or "text" or "char" or "nchar" or "xml" => StandardType.String,
            "json" or "jsonb" => StandardType.Json,
            _ => StandardType.String
        };

        if (stdType == StandardType.Decimal)
        {
            length = null;
        }
        else
        {
            precision = null;
            scale = null;
        }

        return stdType;
    }
}


