using System.Data;
using System.Globalization;
using System.Text;
using TableLens.Core.Models;
using DotNetDBF;

namespace TableLens.Core.Dbf;

public sealed class DbfSchemaService
{
    public DbfSchemaSnapshot CreateEmpty(string filePath, IReadOnlyList<DbfFieldDefinition> definitions)
    {
        var fields = definitions.Select(CreateField).ToArray();
        return CreateSnapshot(Path.GetFileNameWithoutExtension(filePath), fields, definitions, null);
    }

    public DbfSchemaSnapshot Transform(
        DbfTableData source,
        IReadOnlyList<DbfFieldDefinition> definitions,
        Encoding encoding)
    {
        var fields = definitions.Select(CreateField).ToArray();
        return CreateSnapshot(source.Table.TableName, fields, definitions, source, encoding);
    }

    private static DbfSchemaSnapshot CreateSnapshot(
        string tableName,
        IReadOnlyList<DBFField> fields,
        IReadOnlyList<DbfFieldDefinition> definitions,
        DbfTableData? source,
        Encoding? encoding = null)
    {
        var infos = new List<DbfFieldInfo>(fields.Count);
        var table = new DataTable(tableName)
        {
            Locale = CultureInfo.CurrentCulture,
            CaseSensitive = false
        };

        for (var index = 0; index < fields.Count; index++)
        {
            var field = fields[index];
            var columnType = GetColumnType(field.DataType);
            var column = new DataColumn(field.Name, columnType)
            {
                AllowDBNull = true,
                Caption = field.Name
            };
            column.ExtendedProperties["DbfName"] = field.Name;
            column.ExtendedProperties["DbfType"] = field.DataType.ToString();
            table.Columns.Add(column);
            infos.Add(new DbfFieldInfo(
                index + 1,
                field.Name,
                column.ColumnName,
                field.DataType.ToString(),
                columnType.Name,
                field.FieldLength,
                field.DecimalCount,
                false,
                0,
                Describe(field)));
        }

        if (source is not null)
        {
            CopyRows(source, table, fields, definitions, encoding!);
        }

        table.AcceptChanges();
        return new DbfSchemaSnapshot(infos, fields, table);
    }

    private static void CopyRows(
        DbfTableData source,
        DataTable destination,
        IReadOnlyList<DBFField> destinationFields,
        IReadOnlyList<DbfFieldDefinition> definitions,
        Encoding encoding)
    {
        var sourceByName = source.Fields
            .Select((field, index) => new { field.Name, Index = index })
            .ToDictionary(item => item.Name, item => item.Index, StringComparer.OrdinalIgnoreCase);

        foreach (DataRow sourceRow in source.Table.Rows)
        {
            var destinationRow = destination.NewRow();
            for (var index = 0; index < destinationFields.Count; index++)
            {
                var field = destinationFields[index];
                var sourceName = definitions[index].SourceName;
                if (string.IsNullOrWhiteSpace(sourceName) ||
                    !sourceByName.TryGetValue(sourceName, out var sourceIndex))
                {
                    continue;
                }

                destinationRow[index] = ConvertValue(
                    sourceRow[sourceIndex],
                    destination.Columns[index].DataType,
                    field,
                    encoding);
            }
            destination.Rows.Add(destinationRow);
        }
    }

    private static object ConvertValue(object value, Type targetType, DBFField field, Encoding encoding)
    {
        if (value == DBNull.Value)
        {
            return DBNull.Value;
        }

        object converted = targetType == typeof(string)
            ? Convert.ToString(value, CultureInfo.CurrentCulture) ?? string.Empty
            : targetType.IsInstanceOfType(value)
                ? value
                : Convert.ChangeType(value, targetType, CultureInfo.CurrentCulture);

        if (field.DataType == NativeDbType.Char &&
            encoding.GetByteCount(Convert.ToString(converted) ?? string.Empty) > field.FieldLength)
        {
            throw new InvalidDataException(
                $"Значення «{converted}» не вміщується у поле {field.Name} довжиною {field.FieldLength} байт.");
        }

        return converted;
    }

    private static DBFField CreateField(DbfFieldDefinition definition)
    {
        var field = new DBFField
        {
            Name = definition.Name,
            DataType = definition.Type
        };
        if (definition.Type is not (NativeDbType.Date or NativeDbType.Logical))
        {
            field.FieldLength = definition.Length;
        }
        if (definition.Type is NativeDbType.Numeric or NativeDbType.Float)
        {
            field.DecimalCount = definition.DecimalCount;
        }
        return field;
    }

    private static Type GetColumnType(NativeDbType type) => type switch
    {
        NativeDbType.Date => typeof(DateTime),
        NativeDbType.Float => typeof(decimal),
        NativeDbType.Numeric => typeof(decimal),
        NativeDbType.Logical => typeof(bool),
        _ => typeof(string)
    };

    private static string Describe(DBFField field)
    {
        var characteristics = new List<string>();
        if (field.DecimalCount > 0)
        {
            characteristics.Add($"{field.DecimalCount} знаків після коми");
        }
        return characteristics.Count == 0 ? "—" : string.Join(", ", characteristics);
    }
}

public sealed record DbfSchemaSnapshot(
    IReadOnlyList<DbfFieldInfo> Fields,
    IReadOnlyList<DBFField> SourceFields,
    DataTable Table);
