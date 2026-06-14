using System.Data;
using System.Globalization;
using System.Text;
using TableLens.Core.Models;
using DotNetDBF;

namespace TableLens.Core.Dbf;

/// <summary>
/// Читає структуру та значення DBF через DotNetDBF.
/// Якщо бібліотека натрапляє на пошкоджений запис, уже прочитані рядки
/// повертаються як частковий результат замість очищення всієї таблиці.
/// </summary>
public sealed class DbfTableReader
{
    private const int WarningSampleLimit = 50;

    public DbfTableData Read(string filePath, Encoding encoding, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("DBF-файл не знайдено.", filePath);
        }

        DbfFileAccess.EnsureAvailable(filePath);
        var (signature, languageDriver) = ReadHeaderMetadata(filePath);

        using var reader = new DBFReader(filePath)
        {
            CharEncoding = encoding
        };

        var sourceFields = reader.Fields;
        var declaredRecordCount = reader.RecordCount;
        var fields = CreateFieldInfo(sourceFields);
        var table = CreateDataTable(Path.GetFileNameWithoutExtension(filePath), fields, sourceFields);
        var warnings = new List<DbfReadWarning>();
        var warningCount = 0;
        var isPartialRead = false;
        string? partialReadMessage = null;
        var physicalRecordNumber = 0;

        while (true)
        {
            // Скасований запит не повертаємо як успішно прочитану таблицю.
            cancellationToken.ThrowIfCancellationRequested();

            object[]? values;
            try
            {
                // Не обмежуємо цикл reader.RecordCount: у частини DBF-файлів
                // кількість записів у заголовку не оновлена, хоча дані у файлі є.
                values = reader.NextRecord();
            }
            catch (Exception exception)
            {
                isPartialRead = true;
                partialReadMessage = GetInnermostMessage(exception);
                AddWarning(
                    warnings,
                    ref warningCount,
                    physicalRecordNumber + 1,
                    "—",
                    string.Empty,
                    $"Подальше читання зупинено: {partialReadMessage}");
                break;
            }

            if (values is null)
            {
                break;
            }

            physicalRecordNumber++;
            var row = table.NewRow();

            for (var fieldIndex = 0; fieldIndex < sourceFields.Length; fieldIndex++)
            {
                var field = sourceFields[fieldIndex];
                var value = fieldIndex < values.Length ? values[fieldIndex] : null;

                try
                {
                    row[fieldIndex] = ConvertReaderValue(value, table.Columns[fieldIndex]);
                }
                catch (Exception exception)
                {
                    row[fieldIndex] = DBNull.Value;
                    AddWarning(
                        warnings,
                        ref warningCount,
                        physicalRecordNumber,
                        field.Name,
                        SafeValueText(value),
                        $"Поле пропущено: {GetInnermostMessage(exception)}");
                }
            }

            table.Rows.Add(row);
        }

        table.AcceptChanges();

        return new DbfTableData
        {
            FilePath = Path.GetFullPath(filePath),
            Fields = fields,
            SourceFields = sourceFields,
            Table = table,
            DeclaredRecordCount = declaredRecordCount,
            FileSize = new FileInfo(filePath).Length,
            LastWriteTimeUtc = File.GetLastWriteTimeUtc(filePath),
            Signature = signature,
            LanguageDriver = languageDriver,
            ReadWarningCount = warningCount,
            ReadWarnings = warnings,
            IsPartialRead = isPartialRead,
            PartialReadMessage = partialReadMessage
        };
    }

    private static (byte Signature, byte LanguageDriver) ReadHeaderMetadata(string filePath)
    {
        using var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: false);
        var signature = reader.ReadByte();
        stream.Seek(29, SeekOrigin.Begin);
        var languageDriver = reader.ReadByte();
        return (signature, languageDriver);
    }

    private static object ConvertReaderValue(object? value, DataColumn column)
    {
        if (value is null || value == DBNull.Value)
        {
            return DBNull.Value;
        }

        if (column.DataType.IsInstanceOfType(value))
        {
            return value;
        }

        if (column.DataType == typeof(string))
        {
            return value is byte[] bytes
                ? Convert.ToHexString(bytes)
                : value.ToString() ?? string.Empty;
        }

        return Convert.ChangeType(value, column.DataType, CultureInfo.InvariantCulture)
               ?? DBNull.Value;
    }

    private static string SafeValueText(object? value)
    {
        try
        {
            return value switch
            {
                null => string.Empty,
                DBNull _ => string.Empty,
                byte[] bytes => Convert.ToHexString(bytes),
                _ => value.ToString() ?? string.Empty
            };
        }
        catch
        {
            return "<не вдалося відобразити значення>";
        }
    }

    private static string GetInnermostMessage(Exception exception)
    {
        var current = exception;
        while (current.InnerException is not null)
        {
            current = current.InnerException;
        }

        return current.Message;
    }

    private static void AddWarning(
        ICollection<DbfReadWarning> warnings,
        ref int warningCount,
        int recordNumber,
        string fieldName,
        string rawValue,
        string message)
    {
        warningCount++;
        if (warnings.Count < WarningSampleLimit)
        {
            warnings.Add(new DbfReadWarning(recordNumber, fieldName, rawValue, message));
        }
    }

    private static List<DbfFieldInfo> CreateFieldInfo(DBFField[] fields)
    {
        var result = new List<DbfFieldInfo>(fields.Length);
        var usedColumnNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < fields.Length; i++)
        {
            var field = fields[i];
            var dataColumnName = MakeUniqueColumnName(field.Name, i, usedColumnNames);
            var dotNetType = GetColumnType(field).Name;

            result.Add(new DbfFieldInfo(
                i + 1,
                field.Name,
                dataColumnName,
                field.DataType.ToString(),
                dotNetType,
                field.FieldLength,
                field.DecimalCount,
                field.indexFieldFlag != 0,
                field.workAreaId,
                Describe(field)));
        }

        return result;
    }

    private static DataTable CreateDataTable(
        string tableName,
        IReadOnlyList<DbfFieldInfo> fields,
        IReadOnlyList<DBFField> sourceFields)
    {
        var table = new DataTable(tableName)
        {
            Locale = CultureInfo.CurrentCulture,
            CaseSensitive = false
        };

        for (var i = 0; i < fields.Count; i++)
        {
            var column = new DataColumn(fields[i].DataColumnName, GetColumnType(sourceFields[i]))
            {
                AllowDBNull = true,
                Caption = fields[i].Name
            };

            column.ExtendedProperties["DbfName"] = fields[i].Name;
            column.ExtendedProperties["DbfType"] = fields[i].DbfType;
            table.Columns.Add(column);
        }

        return table;
    }

    private static Type GetColumnType(DBFField field) => field.DataType switch
    {
        NativeDbType.Date => typeof(DateTime),
        NativeDbType.Float => typeof(decimal),
        NativeDbType.Numeric => typeof(decimal),
        NativeDbType.Logical => typeof(bool),
        _ => typeof(string)
    };

    private static string MakeUniqueColumnName(string sourceName, int index, ISet<string> usedNames)
    {
        var baseName = string.IsNullOrWhiteSpace(sourceName) ? $"FIELD_{index + 1}" : sourceName.Trim();
        var candidate = baseName;
        var suffix = 2;

        while (!usedNames.Add(candidate))
        {
            candidate = $"{baseName}_{suffix++}";
        }

        return candidate;
    }

    private static string Describe(DBFField field)
    {
        var parts = new List<string>();

        if (field.DecimalCount > 0)
        {
            parts.Add($"знаків після коми: {field.DecimalCount}");
        }

        if (field.indexFieldFlag != 0)
        {
            parts.Add("індексоване");
        }

        if (field.setFieldsFlag != 0)
        {
            parts.Add($"flags: 0x{field.setFieldsFlag:X2}");
        }

        return parts.Count == 0 ? "—" : string.Join("; ", parts);
    }
}
